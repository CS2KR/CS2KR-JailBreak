using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Jailbreak.Config;
using Jailbreak.Core;
using Jailbreak.Features.Teams;
using Jailbreak.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Jailbreak.Features.Orders;

public sealed class GuardOrderManager
{
    private readonly BasePlugin _plugin;
    private readonly RoundManager _roundManager;
    private readonly GuardOrderConfig _config;
    private readonly ILogger _logger;
    private readonly Func<string> _latestIncidentProvider;
    private readonly OutputThrottleManager _outputThrottleManager;
    private readonly Dictionary<ulong, DateTimeOffset> _lastCommandAt = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _hudTimer;
    private readonly Dictionary<ulong, int> _shownHudRevisionBySteamId = new();
    private string _priorityHudText = string.Empty;
    private int _hudRevision;

    public GuardOrderManager(
        BasePlugin plugin,
        RoundManager roundManager,
        GuardOrderConfig config,
        ILogger logger,
        Func<string>? latestIncidentProvider = null,
        OutputThrottleManager? outputThrottleManager = null)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(roundManager);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _plugin = plugin;
        _roundManager = roundManager;
        _config = config;
        _logger = logger;
        _latestIncidentProvider = latestIncidentProvider ?? (() => string.Empty);
        _outputThrottleManager = outputThrottleManager ?? new OutputThrottleManager();
    }

    public GuardOrderState State { get; } = new();

    public bool CanUse(
        CCSPlayerController? player,
        bool hasAdminOverride,
        bool requireActiveRound = true)
    {
        bool result;
        string reason;

        if (!_config.Enabled)
        {
            result = false;
            reason = "guard orders disabled";
        }
        else if (player is null)
        {
            result = false;
            reason = "player null";
        }
        else if (!player.IsValid)
        {
            result = false;
            reason = "player invalid";
        }
        else if (player.IsBot)
        {
            result = false;
            reason = "bot cannot use command";
        }
        else if (player.IsHLTV)
        {
            result = false;
            reason = "hltv cannot use command";
        }
        else if (player.SteamID == 0)
        {
            result = false;
            reason = "missing steam id";
        }
        else if (requireActiveRound &&
            !_roundManager.State.IsActive)
        {
            result = false;
            reason = "round inactive";
        }
        else if (hasAdminOverride)
        {
            result = true;
            reason = "admin override";
        }
        else
        {
            result = TeamManager.IsGuard(player);
            reason = result
                ? "guard"
                : "not guard";
        }

        LogCanUseResult(
            player,
            hasAdminOverride,
            requireActiveRound,
            result,
            reason);

        return result;
    }

    private void LogCanUseResult(
        CCSPlayerController? player,
        bool hasAdminOverride,
        bool requireActiveRound,
        bool result,
        string reason)
    {
        _logger.LogInformation(
            "[CS2.KR] GuardOrderManager.CanUse result. Player: {PlayerName}, Slot: {Slot}, SteamID: {SteamId}, IsBot: {IsBot}, IsHLTV: {IsHLTV}, IsValid: {IsValid}, Team: {Team}, PawnAlive: {PawnAlive}, HasAdminOverride: {HasAdminOverride}, RequireActiveRound: {RequireActiveRound}, RoundActive: {RoundActive}, Round: {RoundNumber}, Result: {Result}, Reason: {Reason}",
            player?.PlayerName ?? "<null>",
            player?.Slot ?? -1,
            player?.SteamID ?? 0,
            player?.IsBot ?? false,
            player?.IsHLTV ?? false,
            player?.IsValid ?? false,
            player?.Team.ToString() ?? "<null>",
            player?.PawnIsAlive ?? false,
            hasAdminOverride,
            requireActiveRound,
            _roundManager.State.IsActive,
            _roundManager.State.RoundNumber,
            result,
            reason);
    }

    public void OpenLocationMenu(
        CCSPlayerController player,
        Func<CCSPlayerController, bool> canStillUse,
        Action onOrderIssued,
        Action<CCSPlayerController> onCustomInputRequested)
    {
        if (!canStillUse(player))
        {
            player.PrintToChat(
                _plugin.Localizer.ForPlayer(player, "order.error.no_permission"));
            return;
        }

        CenterHtmlMenu menu = new(
            _plugin.Localizer.ForPlayer(player, "order.menu.location.title"),
            _plugin)
        {
            ExitButton = true
        };

        foreach (string location in GetMenuLocations())
        {
            string capturedLocation = location;

            menu.AddMenuOption(
                capturedLocation,
                (selectedPlayer, _) =>
                {
                    if (!canStillUse(selectedPlayer))
                    {
                        MenuManager.CloseActiveMenu(selectedPlayer);
                        selectedPlayer.PrintToChat(
                            _plugin.Localizer.ForPlayer(
                                selectedPlayer,
                                "order.error.no_permission"));
                        return;
                    }

                    OpenTimeMenu(
                        selectedPlayer,
                        capturedLocation,
                        canStillUse,
                        onOrderIssued);
                });
        }

        menu.AddMenuOption(
            _plugin.Localizer.ForPlayer(player, "order.menu.custom"),
            (selectedPlayer, _) =>
            {
                MenuManager.CloseActiveMenu(selectedPlayer);

                if (!canStillUse(selectedPlayer))
                {
                    selectedPlayer.PrintToChat(
                        _plugin.Localizer.ForPlayer(
                            selectedPlayer,
                            "order.error.no_permission"));
                    return;
                }

                onCustomInputRequested(selectedPlayer);
            });

        menu.Open(player);
    }

    public bool IssueDirect(
        CCSPlayerController issuer,
        string location,
        int deadlineRoundSeconds,
        out string error)
    {
        error = string.Empty;

        if (!_roundManager.State.IsActive)
        {
            error = "현재 활성화된 라운드가 없습니다.";
            return false;
        }

        int remaining = GetRemainingRoundSeconds();

        if (deadlineRoundSeconds < 0 ||
            deadlineRoundSeconds >= remaining)
        {
            error = "선택한 시간이 현재 남은 라운드 시간보다 작아야 합니다.";
            return false;
        }

        int minimumDeadline = Math.Max(
            0,
            _config.MinimumDeadlineSeconds);

        if (deadlineRoundSeconds < minimumDeadline)
        {
            error = $"선택한 시간이 최소 시간 {FormatRoundTime(minimumDeadline)}보다 작습니다.";
            return false;
        }

        return IssueTimed(
            issuer,
            location,
            deadlineRoundSeconds,
            out error);
    }

    public bool IssueCustomText(
        CCSPlayerController issuer,
        string rawText,
        out string error)
    {
        error = string.Empty;

        if (!_roundManager.State.IsActive)
        {
            error = "현재 활성화된 라운드가 없습니다.";
            return false;
        }

        string text = rawText.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "지시 내용이 비어 있습니다.";
            return false;
        }

        if (IsOnCooldown(
                issuer,
                out float remainingSeconds))
        {
            error =
                $"지시 변경까지 {Math.Ceiling(remainingSeconds)}초 남았습니다.";
            return false;
        }

        State.SetCustom(
            text,
            issuer.SteamID,
            issuer.PlayerName);

        _lastCommandAt[issuer.SteamID] =
            DateTimeOffset.UtcNow;

        BroadcastChat($"[간수 지시] {text}");
        PlayNotificationSound();
        StartHudTimer();

        _logger.LogInformation(
            "[CS2.KR] Custom guard order issued. Text: {Text}, Issuer: {Issuer}, SteamID: {SteamId}",
            text,
            issuer.PlayerName,
            issuer.SteamID);

        return true;
    }

    public bool IssueDollarText(
        CCSPlayerController issuer,
        string text,
        out string error)
    {
        error = string.Empty;

        if (!text.StartsWith('$'))
        {
            error = "$로 시작하는 빠른 시간 지시가 아닙니다.";
            return false;
        }

        string body = text[1..].Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            error = "$ 뒤에 시간과 지시 내용을 입력하세요. 예: $6:30 중앙 앉아서";
            return false;
        }

        int separatorIndex = body.IndexOfAny([' ', '\t']);

        if (separatorIndex <= 0)
        {
            error = "시간 뒤에 지시 내용을 입력하세요. 예: $6:30 중앙 앉아서";
            return false;
        }

        string timeText = body[..separatorIndex].Trim();
        string instruction = body[separatorIndex..].Trim();

        if (string.IsNullOrWhiteSpace(instruction))
        {
            error = "지시 내용이 비어 있습니다.";
            return false;
        }

        if (!TryParseRoundTime(
                timeText,
                out int deadlineRoundSeconds))
        {
            error = "시간 형식이 올바르지 않습니다. 예: $6:30 중앙 앉아서";
            return false;
        }

        int remaining = GetRemainingRoundSeconds();

        if (deadlineRoundSeconds < 0 ||
            deadlineRoundSeconds >= remaining)
        {
            error = "선택한 시간이 현재 남은 라운드 시간보다 작아야 합니다.";
            return false;
        }

        int minimumDeadline = Math.Max(
            0,
            _config.MinimumDeadlineSeconds);

        if (deadlineRoundSeconds < minimumDeadline)
        {
            error = $"선택한 시간이 최소 시간 {FormatRoundTime(minimumDeadline)}보다 작습니다.";
            return false;
        }

        State.SetTimed(
            $"{FormatRoundTime(deadlineRoundSeconds)}까지 {instruction}",
            deadlineRoundSeconds,
            issuer.SteamID,
            issuer.PlayerName);

        _lastCommandAt[issuer.SteamID] =
            DateTimeOffset.UtcNow;

        BroadcastChat($"[간수 지시] {State.DisplayText}");
        PlayNotificationSound();
        StartHudTimer();

        _logger.LogInformation(
            "[CS2.KR] Dollar guard order issued. RawText: {RawText}, Deadline: {Deadline}, Instruction: {Instruction}, Issuer: {Issuer}, SteamID: {SteamId}",
            text,
            FormatRoundTime(deadlineRoundSeconds),
            instruction,
            issuer.PlayerName,
            issuer.SteamID);

        return true;
    }

    public bool SetExtraText(
        CCSPlayerController issuer,
        string rawText,
        out string error)
    {
        error = string.Empty;

        if (!_roundManager.State.IsActive)
        {
            error = "현재 활성화된 라운드가 없습니다.";
            return false;
        }

        string text = rawText.Trim();

        if (text.StartsWith('+'))
        {
            text = text[1..].Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            State.ClearExtra();
            StartHudTimer();
            BroadcastChat("[추가명령] 없음");
            return true;
        }

        State.SetExtra(text);
        StartHudTimer();
        BroadcastChat($"[추가명령] {text}");

        _logger.LogInformation(
            "[CS2.KR] Extra guard order updated. Text: {Text}, Issuer: {Issuer}, SteamID: {SteamId}",
            text,
            issuer.PlayerName,
            issuer.SteamID);

        return true;
    }

    private static bool TryParseRoundTime(
        string text,
        out int seconds)
    {
        seconds = 0;
        string trimmed = text.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        int colonIndex = trimmed.IndexOf(':');

        if (colonIndex >= 0)
        {
            string minuteText = trimmed[..colonIndex];
            string secondText = trimmed[(colonIndex + 1)..];

            if (!int.TryParse(
                    minuteText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int minutes) ||
                !int.TryParse(
                    secondText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int parsedSeconds) ||
                minutes < 0 ||
                parsedSeconds < 0 ||
                parsedSeconds >= 60)
            {
                return false;
            }

            seconds = (minutes * 60) + parsedSeconds;
            return true;
        }

        if (!int.TryParse(
                trimmed,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value) ||
            value < 0)
        {
            return false;
        }

        seconds = value >= 60
            ? value
            : value * 60;

        return true;
    }

    public bool Cancel(
        CCSPlayerController? issuer,
        string reason,
        bool announce = true)
    {
        if (!State.IsActive)
        {
            return false;
        }

        string previousText = State.DisplayText;
        State.ClearPrimary();
        StartHudTimer();

        if (announce)
        {
            BroadcastChat(
                $" CS2.\x0BKR\x01｜ 현재 간수 지시가 취소되었습니다. 사유: {reason}");
        }

        _logger.LogInformation(
            "[CS2.KR] Guard order cancelled. Text: {Text}, Reason: {Reason}, Issuer: {Issuer}, SteamID: {SteamId}",
            previousText,
            reason,
            issuer?.PlayerName ?? "Server",
            issuer?.SteamID ?? 0);

        return true;
    }

    public void Clear()
    {
        State.Clear();
        _priorityHudText = string.Empty;
        _lastCommandAt.Clear();
        StopHudTimer();
        ClearHudForAll();
    }

    // map_end 전용: 맵/엔티티가 언로드되는 중이라 ClearHudForAll의 GetPlayers
    // 조회가 네이티브 크래시를 내므로, 엔티티를 만지지 않고 지시 상태와
    // 타이머만 리셋합니다. HUD는 맵이 끝나는 시점에 지울 필요가 없습니다.
    public void ResetForMapEnd()
    {
        State.Clear();
        _lastCommandAt.Clear();
        StopHudTimer();
        _shownHudRevisionBySteamId.Clear();
    }

    public void DisplayHud()
    {
        RefreshHudState();
    }

    public void SetPriorityHud(string text)
    {
        _priorityHudText = text.Trim();
        StartHudTimer();
    }

    public void ClearPriorityHud()
    {
        if (string.IsNullOrWhiteSpace(_priorityHudText))
        {
            return;
        }

        _priorityHudText = string.Empty;
        StartHudTimer();
    }

    private void RefreshHudState()
    {
        if (!_roundManager.State.IsActive)
        {
            State.Clear();
            StopHudTimer();
            ClearHudForAll();
            return;
        }

        string text = string.IsNullOrWhiteSpace(_priorityHudText)
            ? "[간수 지시]\n" +
              $"자유시간: {GetHudLine(State.PrimaryText)}\n" +
              $"추가명령: {GetHudLine(State.ExtraText)}\n" +
              $"최근사건: {GetHudLine(_latestIncidentProvider())}"
            : _priorityHudText;

        foreach (CCSPlayerController player
                 in Utilities.GetPlayers())
        {
            if (!IsValidHumanPlayer(player))
            {
                continue;
            }

            ulong steamId = player.SteamID;

            if (MenuManager.GetActiveMenu(player) is not null)
            {
                // 메뉴가 닫힌 뒤 한 번만 다시 출력하도록 표시 기록을 제거합니다.
                _shownHudRevisionBySteamId.Remove(steamId);
                continue;
            }

            player.PrintToCenter(text);
            _shownHudRevisionBySteamId[steamId] = _hudRevision;
        }
    }

    private void ClearHudForAll()
    {
        _shownHudRevisionBySteamId.Clear();

        foreach (CCSPlayerController player
                 in Utilities.GetPlayers())
        {
            if (!IsValidHumanPlayer(player) ||
                MenuManager.GetActiveMenu(player) is not null)
            {
                continue;
            }

            player.PrintToCenter(" ");
            player.PrintToCenterHtml(" ", 1);
        }
    }

    public int GetRemainingRoundSeconds()
    {
        DateTimeOffset? startedAt =
            _roundManager.State.StartedAtUtc;

        if (!_roundManager.State.IsActive ||
            startedAt is null)
        {
            return 0;
        }

        int elapsed = (int)Math.Floor(
            (DateTimeOffset.UtcNow -
             startedAt.Value).TotalSeconds);

        return Math.Max(
            0,
            _config.RoundDurationSeconds - elapsed);
    }

    public static string FormatRoundTime(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    private void StartHudTimer()
    {
        StopHudTimer();
        _hudRevision++;
        _shownHudRevisionBySteamId.Clear();

        float refresh = Math.Clamp(
            _config.HudRefreshSeconds,
            0.25f,
            1.0f);

        // 메뉴 선택 콜백 종료 후 CenterHtmlMenu가 보내는 빈 메시지보다
        // 한 프레임 늦게 지시 HUD를 출력합니다.
        Server.NextFrame(RefreshHudState);

        // CS2 CenterHtml은 긴 duration을 줘도 클라이언트 상태에 따라 짧게 사라질 수 있어
        // 활성 지시는 짧은 패킷을 반복 전송해 유지합니다.
        _hudTimer = _plugin.AddTimer(
            refresh,
            RefreshHudState,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StopHudTimer()
    {
        _hudTimer?.Kill();
        _hudTimer = null;
    }

    private static string GetHudLine(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? "없음"
            : text.Trim();
    }

    private void OpenTimeMenu(
        CCSPlayerController player,
        string location,
        Func<CCSPlayerController, bool> canStillUse,
        Action onOrderIssued)
    {
        if (!canStillUse(player))
        {
            MenuManager.CloseActiveMenu(player);
            player.PrintToChat(
                _plugin.Localizer.ForPlayer(player, "order.error.no_permission"));
            return;
        }

        List<int> options = BuildTimeOptions();

        if (options.Count == 0)
        {
            MenuManager.CloseActiveMenu(player);
            player.PrintToChat(
                " CS2.\x0BKR\x01｜ 현재 선택할 수 있는 지시 시간이 없습니다.");
            return;
        }

        CenterHtmlMenu menu = new(
            _plugin.Localizer.ForPlayer(
                player,
                "order.menu.time.title",
                location),
            _plugin)
        {
            ExitButton = true
        };

        foreach (int deadlineSeconds in options)
        {
            int capturedDeadline = deadlineSeconds;

            menu.AddMenuOption(
                _plugin.Localizer.ForPlayer(
                    player,
                    "order.menu.time.option",
                    FormatRoundTime(capturedDeadline)),
                (selectedPlayer, _) =>
                {
                    MenuManager.CloseActiveMenu(selectedPlayer);

                    if (!canStillUse(selectedPlayer))
                    {
                        selectedPlayer.PrintToChat(
                            _plugin.Localizer.ForPlayer(
                                selectedPlayer,
                                "order.error.no_permission"));
                        return;
                    }

                    if (!IssueTimed(
                            selectedPlayer,
                            location,
                            capturedDeadline,
                            out string error))
                    {
                        selectedPlayer.PrintToChat(
                            $" CS2.\x0BKR\x01｜ 지시 실패: {error}");
                        return;
                    }

                    onOrderIssued();
                });
        }

        menu.Open(player);
    }

    private List<int> BuildTimeOptions()
    {
        int remaining = GetRemainingRoundSeconds();
        int step = Math.Max(
            1,
            _config.TimeStepSeconds);

        int firstDeadline =
            ((remaining - 1) / step) * step;

        List<int> result = new();

        int minimumDeadline = Math.Max(
            0,
            _config.MinimumDeadlineSeconds);

        for (int deadline = firstDeadline;
             deadline >= minimumDeadline &&
             result.Count < _config.TimeOptionCount;
             deadline -= step)
        {
            result.Add(deadline);
        }

        return result;
    }

    private bool IssueTimed(
        CCSPlayerController issuer,
        string location,
        int deadlineRoundSeconds,
        out string error)
    {
        error = string.Empty;
        location = location.Trim();

        if (!_roundManager.State.IsActive)
        {
            error = "현재 활성화된 라운드가 없습니다.";
            return false;
        }

        int remaining = GetRemainingRoundSeconds();

        if (deadlineRoundSeconds < 0 ||
            deadlineRoundSeconds >= remaining)
        {
            error = "선택한 시간이 현재 남은 라운드 시간보다 작아야 합니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            error = "목적지가 비어 있습니다.";
            return false;
        }

        int minimumDeadline = Math.Max(
            0,
            _config.MinimumDeadlineSeconds);

        if (deadlineRoundSeconds < minimumDeadline)
        {
            error = $"선택한 시간이 최소 시간 {FormatRoundTime(minimumDeadline)}보다 작습니다.";
            return false;
        }

        if (IsOnCooldown(
                issuer,
                out float remainingSeconds))
        {
            error =
                $"지시 변경까지 {Math.Ceiling(remainingSeconds)}초 남았습니다.";
            return false;
        }

        string text = FormatOrderText(
            location,
            deadlineRoundSeconds);

        State.SetTimed(
            text,
            deadlineRoundSeconds,
            issuer.SteamID,
            issuer.PlayerName);

        _lastCommandAt[issuer.SteamID] =
            DateTimeOffset.UtcNow;

        BroadcastChat($"[간수 지시] {text}");
        PlayNotificationSound();
        StartHudTimer();

        _logger.LogInformation(
            "[CS2.KR] Guard order issued. Location: {Location}, Deadline: {Deadline}, Issuer: {Issuer}, SteamID: {SteamId}",
            location,
            FormatRoundTime(deadlineRoundSeconds),
            issuer.PlayerName,
            issuer.SteamID);

        return true;
    }

    private bool IsOnCooldown(
        CCSPlayerController player,
        out float remainingSeconds)
    {
        remainingSeconds = 0;

        if (_config.CommandCooldownSeconds <= 0 ||
            !_lastCommandAt.TryGetValue(
                player.SteamID,
                out DateTimeOffset lastUsed))
        {
            return false;
        }

        double elapsed =
            (DateTimeOffset.UtcNow -
             lastUsed).TotalSeconds;

        remainingSeconds = Math.Max(
            0,
            _config.CommandCooldownSeconds -
            (float)elapsed);

        return remainingSeconds > 0;
    }

    private string FormatOrderText(
        string location,
        int deadlineRoundSeconds)
    {
        return _config.OrderTextFormat
            .Replace(
                "{location}",
                location,
                StringComparison.Ordinal)
            .Replace(
                "{time}",
                FormatRoundTime(
                    deadlineRoundSeconds),
                StringComparison.Ordinal);
    }

    private IEnumerable<string> GetLocations()
    {
        return _config.DefaultLocations
            .Where(location =>
                !string.IsNullOrWhiteSpace(location) &&
                !IsHiddenLocation(location))
            .Select(location => location.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetMenuLocations()
    {
        return GetLocations()
            .Take(4);
    }

    private static bool IsHiddenLocation(string location)
    {
        return string.Equals(
            location.Trim(),
            "감방",
            StringComparison.OrdinalIgnoreCase);
    }

    private void BroadcastChat(string message)
    {
        foreach (CCSPlayerController player
                 in Utilities.GetPlayers())
        {
            if (IsValidHumanPlayer(player))
            {
                player.PrintToChat(message);
            }
        }
    }

    private void PlayNotificationSound()
    {
        if (string.IsNullOrWhiteSpace(
                _config.NotificationSound))
        {
            return;
        }

        TimeSpan cooldown = TimeSpan.FromSeconds(
            Math.Max(0, _config.NotificationSoundCooldownSeconds));

        if (!_outputThrottleManager.TryAcquire(
                "guard_order_notification_sound",
                cooldown))
        {
            return;
        }

        foreach (CCSPlayerController player
                 in Utilities.GetPlayers())
        {
            if (!IsValidHumanPlayer(player))
            {
                continue;
            }

            CCSPlayerPawn? pawn =
                player.PlayerPawn.Value;

            if (pawn is null || !pawn.IsValid)
            {
                continue;
            }

            try
            {
                pawn.EmitSound(
                    _config.NotificationSound);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "[CS2.KR] Guard order notification sound failed. Sound: {Sound}",
                    _config.NotificationSound);
                return;
            }
        }
    }

    private static bool IsValidHumanPlayer(
        CCSPlayerController player)
    {
        return TeamManager.IsHumanPlayer(player);
    }
}
