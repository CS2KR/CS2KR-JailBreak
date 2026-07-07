using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Jailbreak.Config;
using Jailbreak.Core;
using Jailbreak.Features.Countdown;
using Jailbreak.Features.Freeday;
using Jailbreak.Features.Freekill;
using Jailbreak.Features.Incidents;
using Jailbreak.Features.Orders;
using Jailbreak.Features.Rebel;
using Jailbreak.Features.Teams;
using Microsoft.Extensions.Logging;

namespace Jailbreak;

[MinimumApiVersion(80)]
public sealed class JailbreakPlugin : BasePlugin, IPluginConfig<JailbreakConfig>
{
    private static readonly string[] DefaultGuardOrderLocations =
    [
        "중앙",
        "수영장",
        "샤워실",
        "운동장",
        "감방"
    ];

    private readonly string _pluginInstanceId =
        Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<int, ulong> _stateKeysBySlot = new();
    private readonly Dictionary<ulong, DateTimeOffset> _awaitingCustomOrderInput = new();
    private readonly HashSet<string> _pendingFreekillEvaluationReasons =
        new(StringComparer.OrdinalIgnoreCase);

    private RoundManager? _roundManager;
    private PlayerStateManager? _playerStateManager;
    private GuardRatioManager? _guardRatioManager;
    private TeamSwapManager? _teamSwapManager;
    private RebelManager? _rebelManager;
    private FreedayManager? _freedayManager;
    private FreekillTimeManager? _freekillTimeManager;
    private CountdownManager? _countdownManager;
    private IncidentLogManager? _incidentLogManager;
    private GuardOrderManager? _guardOrderManager;
    private OutputThrottleManager? _outputThrottleManager;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _autoFreedayTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _freedayHudTimer;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _rebelColorTimer;
    private bool _freekillEvaluationPending;

    public JailbreakConfig Config { get; set; } = new();

    public override string ModuleName => "Jailbreak";
    public override string ModuleVersion => "0.2.23";
    public override string ModuleAuthor => "Dang Geun";
    public override string ModuleDescription => "CS2 Jailbreak game mode core plugin";

    public void OnConfigParsed(JailbreakConfig config)
    {
        if (config.PrisonersPerGuard < 1)
        {
            Logger.LogWarning(
                "[CS2.KR] Invalid PrisonersPerGuard value: {Value}. Using default value: 3.",
                config.PrisonersPerGuard);

            config.PrisonersPerGuard = 3;
        }

        config.AdminPermissions =
            (config.AdminPermissions ?? [])
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Select(permission => permission.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (config.AdminPermissions.Count == 0)
        {
            config.AdminPermissions.Add("@jailbreak/admin");
        }

        if (config.CustomOrderInputTimeoutSeconds < 5)
        {
            config.CustomOrderInputTimeoutSeconds = 15;
        }

        if (config.AutoFreedayDurationSeconds < 0)
        {
            config.AutoFreedayDurationSeconds = 0;
        }

        if (config.TeamSwapRequestTimeoutSeconds < 5)
        {
            config.TeamSwapRequestTimeoutSeconds = 20;
        }

        if (config.TeamSwapDeclineCooldownSeconds < 0)
        {
            config.TeamSwapDeclineCooldownSeconds = 0;
        }

        config.GuardOrders ??= new GuardOrderConfig();

        if (config.GuardOrders.RoundDurationSeconds < 60)
        {
            config.GuardOrders.RoundDurationSeconds = 480;
        }

        if (config.GuardOrders.TimeStepSeconds < 1)
        {
            config.GuardOrders.TimeStepSeconds = 30;
        }

        if (config.GuardOrders.TimeOptionCount < 1)
        {
            config.GuardOrders.TimeOptionCount = 5;
        }

        if (config.GuardOrders.MinimumDeadlineSeconds < 0)
        {
            config.GuardOrders.MinimumDeadlineSeconds = 0;
        }

        if (config.GuardOrders.CommandCooldownSeconds < 0)
        {
            config.GuardOrders.CommandCooldownSeconds = 0;
        }

        if (config.GuardOrders.HudRefreshSeconds <= 0)
        {
            config.GuardOrders.HudRefreshSeconds = 0.5f;
        }

        if (config.GuardOrders.HudPacketDurationSeconds < 1)
        {
            config.GuardOrders.HudPacketDurationSeconds = 2;
        }

        if (string.IsNullOrWhiteSpace(config.GuardOrders.OrderTextFormat))
        {
            config.GuardOrders.OrderTextFormat =
                "{location}로 {time}까지 이동";
        }

        config.GuardOrders.NotificationSound =
            config.GuardOrders.NotificationSound?.Trim()
            ?? string.Empty;

        if (config.GuardOrders.NotificationSoundCooldownSeconds < 0)
        {
            config.GuardOrders.NotificationSoundCooldownSeconds = 0;
        }

        config.GuardOrders.DefaultLocations =
            (config.GuardOrders.DefaultLocations
                ?? [])
                .Where(location => !string.IsNullOrWhiteSpace(location))
                .Select(location => location.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (config.GuardOrders.DefaultLocations.Count == 0)
        {
            config.GuardOrders.DefaultLocations =
                DefaultGuardOrderLocations.ToList();
        }

        Config = config;

        Logger.LogInformation(
            "[CS2.KR] Configuration loaded. PrisonersPerGuard: {PrisonersPerGuard}, DisableRadar: {DisableRadar}, DisableRadarCommand: {DisableRadarCommand}, AutoFreedayEnabled: {AutoFreedayEnabled}, AutoFreedayDurationSeconds: {AutoFreedayDurationSeconds}, TeamSwapRequestTimeoutSeconds: {TeamSwapRequestTimeoutSeconds}, TeamSwapDeclineCooldownSeconds: {TeamSwapDeclineCooldownSeconds}, GuardOrdersEnabled: {Enabled}, RoundDurationSeconds: {RoundDurationSeconds}, TimeStepSeconds: {TimeStepSeconds}",
            Config.PrisonersPerGuard,
            Config.DisableRadar,
            Config.DisableRadarCommand,
            Config.AutoFreedayEnabled,
            Config.AutoFreedayDurationSeconds,
            Config.TeamSwapRequestTimeoutSeconds,
            Config.TeamSwapDeclineCooldownSeconds,
            Config.GuardOrders.Enabled,
            Config.GuardOrders.RoundDurationSeconds,
            Config.GuardOrders.TimeStepSeconds);
    }

    public override void Load(bool hotReload)
    {
        _roundManager = new RoundManager(Logger, _pluginInstanceId);
        _playerStateManager = new PlayerStateManager();
        _guardRatioManager = new GuardRatioManager(Config.PrisonersPerGuard);
        _teamSwapManager = new TeamSwapManager(
            Logger,
            TimeSpan.FromSeconds(Config.TeamSwapRequestTimeoutSeconds),
            TimeSpan.FromSeconds(Config.TeamSwapDeclineCooldownSeconds));
        _rebelManager = new RebelManager(_playerStateManager, Logger);
        _freedayManager = new FreedayManager(
            _playerStateManager,
            _roundManager,
            Logger);
        _freekillTimeManager = new FreekillTimeManager();
        _countdownManager = new CountdownManager(this, Logger);
        _incidentLogManager = new IncidentLogManager();
        _outputThrottleManager = new OutputThrottleManager();

        _guardOrderManager = new GuardOrderManager(
            this,
            _roundManager,
            Config.GuardOrders,
            Logger,
            () => _incidentLogManager?.GetLatestText() ?? string.Empty,
            _outputThrottleManager);

        ApplyRadarPolicy("plugin load");

        RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

        AddCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        AddCommandListener("say", OnCustomOrderChatCommand, HookMode.Pre);
        AddCommandListener("say_team", OnCustomOrderChatCommand, HookMode.Pre);

        AddCommand(
            "css_jb",
            "Jailbreak main command.",
            OnJailbreakCommand);

        StartRebelColorTimer();

        if (hotReload)
        {
            TrackConnectedPlayers();
            AddTimer(
                0.5f,
                () => TryRecoverRoundState(
                    "plugin hot reload"),
                TimerFlags.STOP_ON_MAPCHANGE);
        }

        Logger.LogInformation(
            "[CS2.KR] Plugin loaded. PluginInstance: {PluginInstance}, HotReload: {HotReload}, ModulePath: {ModulePath}",
            _pluginInstanceId,
            hotReload,
            ModulePath);
    }

    public override void Unload(bool hotReload)
    {
        DeregisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
        DeregisterEventHandler<EventRoundStart>(OnRoundStart);
        DeregisterEventHandler<EventRoundEnd>(OnRoundEnd);
        DeregisterEventHandler<EventPlayerTeam>(OnPlayerTeam);
        DeregisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        DeregisterEventHandler<EventPlayerDeath>(OnPlayerDeath);

        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnMapEnd>(OnMapEnd);
        RemoveListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RemoveListener<Listeners.OnClientDisconnect>(OnClientDisconnect);

        RemoveCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        RemoveCommandListener("say", OnCustomOrderChatCommand, HookMode.Pre);
        RemoveCommandListener("say_team", OnCustomOrderChatCommand, HookMode.Pre);
        RemoveCommand("css_jb", OnJailbreakCommand);

        StopAutoFreedayTimer();
        StopFreedayHudTimer();
        StopRebelColorTimer();

        // 핫리로드처럼 맵이 로드된 상태에서는 반란자 색·지시 HUD를 정상
        // 복원하지만, 맵 언로드와 겹친 시점(MapName=null)엔 엔티티 조회가
        // 네이티브 크래시를 내므로 엔티티를 만지지 않는 리셋만 수행합니다.
        bool worldLoaded = !string.IsNullOrEmpty(Server.MapName);

        if (worldLoaded)
        {
            _rebelManager?.RestoreAllRebels();
        }

        _roundManager?.Shutdown();
        _roundManager = null;

        _playerStateManager?.Clear();
        _playerStateManager = null;
        _guardRatioManager = null;
        _teamSwapManager?.Clear();
        _teamSwapManager = null;
        _rebelManager = null;
        _freedayManager = null;

        if (worldLoaded)
        {
            _guardOrderManager?.Clear();
        }
        else
        {
            _guardOrderManager?.ResetForMapEnd();
        }

        _guardOrderManager = null;
        _outputThrottleManager?.Clear();
        _outputThrottleManager = null;
        _stateKeysBySlot.Clear();
        _awaitingCustomOrderInput.Clear();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;

        Logger.LogInformation(
            "[CS2.KR] Plugin unloaded. PluginInstance: {PluginInstance}, HotReload: {HotReload}",
            _pluginInstanceId,
            hotReload);
    }

    private void OnStartGlobalFreedayCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        if (_freekillTimeManager?.IsActive == true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 프리킬 시간 중에는 전체 자유시간을 시작할 수 없습니다.");
            return;
        }

        _guardOrderManager?.Cancel(
            player,
            "전체 자유시간 시작",
            announce: false);
        _freekillTimeManager?.Reset();        StopAutoFreedayTimer();

        if (_freedayManager?.StartGlobalFreeday() != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 자유시간을 시작할 수 없습니다. 라운드 상태 또는 기존 자유시간을 확인하세요.");
            return;
        }

        _freedayManager?.ClearPersonalFreedays(
            "전체 자유시간 시작",
            announce: false);

        StartFreedayHudTimer();

        commandInfo.ReplyToCommand(
            " CS2.\x0BKR\x01｜ 전체 자유시간을 시작했습니다.");
    }

    private void OnEndGlobalFreedayCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        if (_freedayManager?.EndGlobalFreeday("관리자 종료") != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 현재 진행 중인 전체 자유시간이 없습니다.");
            return;
        }

        StopAutoFreedayTimer();
        StopFreedayHudTimer();
        CaptureFreekillBaseline();

        commandInfo.ReplyToCommand(
            " CS2.\x0BKR\x01｜ 전체 자유시간을 종료했습니다.");
    }

    private void OnGrantPersonalFreedayCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        string targetQuery = GetRootTarget(commandInfo);

        if (string.IsNullOrWhiteSpace(targetQuery))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 사용법: css_jb freeday give <플레이어 이름 또는 SteamID64>");
            return;
        }

        CCSPlayerController? target = _freedayManager?.FindTarget(targetQuery);

        if (target is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상 플레이어를 한 명으로 특정할 수 없습니다.");
            return;
        }

        if (!AdminManager.CanPlayerTarget(player, target))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상의 관리자 면역도가 더 높아 변경할 수 없습니다.");
            return;
        }

        if (_freedayManager?.GrantPersonalFreeday(target) != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 개인 프리데이를 적용할 수 없습니다. 라운드 상태, 전체 자유시간, 대상 팀, 반란자 또는 기존 프리데이 여부를 확인하세요.");
            return;
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ {target.PlayerName}에게 개인 프리데이를 적용했습니다.");
    }

    private void OnRemovePersonalFreedayCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        string targetQuery = GetRootTarget(commandInfo);

        if (string.IsNullOrWhiteSpace(targetQuery))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 사용법: css_jb freeday remove <플레이어 이름 또는 SteamID64>");
            return;
        }

        CCSPlayerController? target = _freedayManager?.FindTarget(targetQuery);

        if (target is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상 플레이어를 한 명으로 특정할 수 없습니다.");
            return;
        }

        if (!AdminManager.CanPlayerTarget(player, target))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상의 관리자 면역도가 더 높아 변경할 수 없습니다.");
            return;
        }

        if (_freedayManager?.RemovePersonalFreeday(
                target,
                "관리자 해제") != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 해당 플레이어에게 적용된 개인 프리데이가 없습니다.");
            return;
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ {target.PlayerName}의 개인 프리데이를 해제했습니다.");
    }

    private void OnFreedayListCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        if (_roundManager?.State.IsFreedayRound == true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 현재 전체 자유시간이 진행 중입니다.");
        }

        IReadOnlyList<string> personalFreedays =
            _freedayManager?.GetPersonalFreedayNames()
            ?? [];

        if (personalFreedays.Count == 0)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 현재 개인 프리데이 대상자가 없습니다.");
            return;
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ 개인 프리데이 대상자: {string.Join(", ", personalFreedays)}");
    }

    private void OnJailbreakCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        string section = commandInfo.GetArg(1).Trim().ToLowerInvariant();
        string action = commandInfo.GetArg(2).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(section) ||
            section is "help" or "도움말")
        {
            PrintHelp(commandInfo.ReplyToCommand);
            return;
        }

        switch (section)
        {
            case "status":
            case "상태":
                OnStatusCommand(player, commandInfo);
                return;

            case "reset":
            case "초기화":
                OnResetStateCommand(player, commandInfo);
                return;

            case "version":
            case "test":
            case "버전":
                commandInfo.ReplyToCommand(
                    $"[CS2.KR] Plugin is running. Version: {ModuleVersion}, Instance: {_pluginInstanceId}, ModulePath: {ModulePath}");
                return;

            case "order":
            case "지시":
                if (action is "cancel" or "취소")
                {
                    OnCancelGuardOrderCommand(player, commandInfo);
                }
                else
                {
                    OnGuardOrderMenuCommand(player, commandInfo);
                }
                return;

            case "countdown":
            case "count":
            case "5":
            case "카운트":
                OnCountdownCommand(player, commandInfo, 5);
                return;

            case "incidents":
            case "incident":
            case "events":
            case "사건":
            case "최근사건":
                OnIncidentsCommand(player, commandInfo);
                return;

            case "team":
            case "팀":
                switch (action)
                {
                    case "swap":
                    case "exchange":
                    case "교환":
                        OnTeamSwapRequestCommand(player, commandInfo);
                        return;
                    case "accept":
                    case "yes":
                    case "수락":
                        OnTeamSwapAcceptCommand(player, commandInfo);
                        return;
                    case "deny":
                    case "decline":
                    case "no":
                    case "거절":
                        OnTeamSwapDeclineCommand(player, commandInfo);
                        return;
                    default:
                        commandInfo.ReplyToCommand(
                            " CS2.\x0BKR\x01｜ 사용법: css_jb team <swap|accept|deny> [대상]");
                        return;
                }

            case "freeday":
            case "자유시간":
                switch (action)
                {
                    case "start":
                    case "시작":
                        OnStartGlobalFreedayCommand(player, commandInfo);
                        return;
                    case "end":
                    case "stop":
                    case "종료":
                        OnEndGlobalFreedayCommand(player, commandInfo);
                        return;
                    case "give":
                    case "add":
                    case "지정":
                        OnGrantPersonalFreedayCommand(player, commandInfo);
                        return;
                    case "remove":
                    case "delete":
                    case "해제":
                        OnRemovePersonalFreedayCommand(player, commandInfo);
                        return;
                    case "list":
                    case "목록":
                        OnFreedayListCommand(player, commandInfo);
                        return;
                    default:
                        commandInfo.ReplyToCommand(
                            " CS2.\x0BKR\x01｜ 사용법: css_jb freeday <start|end|give|remove|list> [대상]");
                        return;
                }

            case "rebel":
            case "반란":
                switch (action)
                {
                    case "add":
                    case "give":
                    case "지정":
                        OnMarkRebelCommand(player, commandInfo);
                        return;
                    case "remove":
                    case "delete":
                    case "해제":
                        OnUnmarkRebelCommand(player, commandInfo);
                        return;
                    case "list":
                    case "목록":
                        OnRebelListCommand(player, commandInfo);
                        return;
                    default:
                        commandInfo.ReplyToCommand(
                            " CS2.\x0BKR\x01｜ 사용법: css_jb rebel <add|remove|list> [대상]");
                        return;
                }

            default:
                commandInfo.ReplyToCommand(
                    " CS2.\x0BKR\x01｜ 알 수 없는 하위 명령입니다. css_jb help를 확인하세요.");
                return;
        }
    }

    private HookResult OnCustomOrderChatCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!CanUseCommand(player))
        {
            return HookResult.Continue;
        }

        CCSPlayerController trackedPlayer = player!;
        string text = NormalizeChatText(commandInfo.ArgString);

        if (string.IsNullOrWhiteSpace(text))
        {
            return HookResult.Continue;
        }

        if (_awaitingCustomOrderInput.TryGetValue(
                trackedPlayer.SteamID,
                out DateTimeOffset expiresAt))
        {
            _awaitingCustomOrderInput.Remove(trackedPlayer.SteamID);

            if (DateTimeOffset.UtcNow > expiresAt)
            {
                trackedPlayer.PrintToChat(
                    " CS2.\x0BKR\x01｜ 자유 지시 입력 시간이 만료되었습니다. !지시로 다시 시작하세요.");
            }
            else if (text is "!취소" or "/취소" or "!cancel" or "/cancel")
            {
                trackedPlayer.PrintToChat(
                    " CS2.\x0BKR\x01｜ 자유 지시 입력을 취소했습니다.");
                return HookResult.Handled;
            }
            else
            {
                bool adminOverride =
                    HasAdminPermission(trackedPlayer);

                if (!EnsureGuardOrderRoundUsable(
                        trackedPlayer,
                        adminOverride,
                        "custom guard order input") ||
                    _guardOrderManager?.CanUse(
                        trackedPlayer,
                        adminOverride) != true)
                {
                    trackedPlayer.PrintToChat(
                        " CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 지시할 수 있습니다.");
                    return HookResult.Handled;
                }

                if (_guardOrderManager is null)
                {
                    trackedPlayer.PrintToChat(
                        " CS2.\x0BKR\x01｜ 자유 지시 관리자가 초기화되지 않았습니다.");
                    return HookResult.Handled;
                }

                if (!_guardOrderManager.IssueCustomText(
                        trackedPlayer,
                        text,
                        out string error))
                {
                    trackedPlayer.PrintToChat(
                        $" CS2.\x0BKR\x01｜ 자유 지시 실패: {error}");
                    return HookResult.Handled;
                }

                if (_freedayManager?.EndGlobalFreeday(
                        "새 간수 지시") == true)
                {
                    StopAutoFreedayTimer();
                    StopFreedayHudTimer();
                }

                return HookResult.Handled;
            }
        }

        string normalized = text.ToLowerInvariant();

        if (text.StartsWith('$'))
        {
            IssueDollarGuardOrder(trackedPlayer, text);
            return HookResult.Handled;
        }

        if (text.StartsWith('+'))
        {
            IssueExtraGuardOrder(trackedPlayer, text);
            return HookResult.Handled;
        }

        if (normalized is "!명령" or "/명령" or "!지시" or "/지시" or "!order" or "/order")
        {
            LogGuardOrderCommandAttempt(
                "chat",
                trackedPlayer,
                tryRecoverResult: null,
                canUseResult: null);

            OpenGuardOrderMenu(trackedPlayer);
            return HookResult.Handled;
        }

        if (normalized is "!5" or "/5")
        {
            StartGuardCountdown(trackedPlayer, 5, "시작", trackedPlayer.PrintToChat);
            return HookResult.Handled;
        }

        if (TryHandleTeamSwapChatCommand(trackedPlayer, text))
        {
            return HookResult.Handled;
        }

        if (normalized is "!도움말" or "/도움말" or "!jbhelp" or "/jbhelp")
        {
            PrintHelp(trackedPlayer.PrintToChat);
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private void IssueDollarGuardOrder(
        CCSPlayerController player,
        string text)
    {
        bool adminOverride =
            HasAdminPermission(player);

        if (!EnsureGuardOrderRoundUsable(
                player,
                adminOverride,
                "dollar guard order") ||
            _guardOrderManager?.CanUse(
                player,
                adminOverride) != true)
        {
            player.PrintToChat(
                " CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 지시할 수 있습니다.");
            return;
        }

        if (_guardOrderManager is null)
        {
            player.PrintToChat(
                " CS2.\x0BKR\x01｜ 간수 지시 관리자가 초기화되지 않았습니다.");
            return;
        }

        if (!_guardOrderManager.IssueDollarText(
                player,
                text,
                out string error))
        {
            player.PrintToChat(
                $" CS2.\x0BKR\x01｜ 빠른 지시 실패: {error}");
            return;
        }

        if (_freedayManager?.EndGlobalFreeday(
                "새 간수 지시") == true)
        {
            StopAutoFreedayTimer();
            StopFreedayHudTimer();
        }
    }

    private void IssueExtraGuardOrder(
        CCSPlayerController player,
        string text)
    {
        bool adminOverride =
            HasAdminPermission(player);

        if (!EnsureGuardOrderRoundUsable(
                player,
                adminOverride,
                "extra guard order") ||
            _guardOrderManager?.CanUse(
                player,
                adminOverride) != true)
        {
            player.PrintToChat(
                " CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 추가명령을 사용할 수 있습니다.");
            return;
        }

        if (_guardOrderManager is null)
        {
            player.PrintToChat(
                " CS2.\x0BKR\x01｜ 간수 지시 관리자가 초기화되지 않았습니다.");
            return;
        }

        if (!_guardOrderManager.SetExtraText(
                player,
                text,
                out string error))
        {
            player.PrintToChat(
                $" CS2.\x0BKR\x01｜ 추가명령 실패: {error}");
            return;
        }
    }

    private static string NormalizeChatText(string rawText)
    {
        string text = rawText.Trim();

        if (text.Length >= 2 &&
            text[0] == '"' &&
            text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    private static string GetRootTarget(CommandInfo commandInfo)
    {
        return JoinArguments(commandInfo, 3);
    }

    private static string JoinArguments(
        CommandInfo commandInfo,
        int startIndex)
    {
        if (commandInfo.ArgCount <= startIndex)
        {
            return string.Empty;
        }

        List<string> parts = new();

        for (int index = startIndex;
             index < commandInfo.ArgCount;
             index++)
        {
            string part = commandInfo.GetArg(index);

            if (!string.IsNullOrWhiteSpace(part))
            {
                parts.Add(part);
            }
        }

        return string.Join(" ", parts).Trim();
    }

    private void OnStatusCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is not null &&
            (!player.IsValid || player.IsBot || player.IsHLTV))
        {
            return;
        }

        string roundState =
            _roundManager?.State.IsActive == true
                ? $"라운드 진행 중 #{_roundManager.State.RoundNumber}"
                : "라운드 없음";

        string freedayState =
            _roundManager?.State.IsFreedayRound == true
                ? "전체 자유시간 진행 중"
                : "전체 자유시간 없음";

        string orderState =
            _guardOrderManager?.State.IsActive == true
                ? $"간수 지시 진행 중: {_guardOrderManager.State.DisplayText}"
                : "간수 지시 없음";

        int personalFreedayCount =
            _freedayManager?.GetPersonalFreedayNames().Count
            ?? 0;

        int rebelCount =
            _rebelManager?.GetRebelNames().Count
            ?? 0;

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ {roundState} / {freedayState} / 프리킬: {_freekillTimeManager?.StatusText ?? "없음"} / 개인 프리데이: {personalFreedayCount}명 / 반란자: {rebelCount}명 / {orderState} / 카운트다운: {_countdownManager?.StatusText ?? "없음"}");

        int alivePrisoners = 0;
        int aliveGuards = 0;
        int botPrisoners = 0;
        int botGuards = 0;
        int humanPrisoners = 0;
        int humanGuards = 0;

        foreach (CCSPlayerController connectedPlayer in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(connectedPlayer))
            {
                continue;
            }

            bool isPrisoner = TeamManager.IsPrisoner(connectedPlayer);
            bool isGuard = TeamManager.IsGuard(connectedPlayer);

            if (connectedPlayer.PawnIsAlive)
            {
                if (isPrisoner)
                {
                    alivePrisoners++;
                }
                else if (isGuard)
                {
                    aliveGuards++;
                }
            }

            if (connectedPlayer.IsBot)
            {
                if (isPrisoner)
                {
                    botPrisoners++;
                }
                else if (isGuard)
                {
                    botGuards++;
                }
            }
            else if (TeamManager.IsHumanPlayer(connectedPlayer))
            {
                if (isPrisoner)
                {
                    humanPrisoners++;
                }
                else if (isGuard)
                {
                    humanGuards++;
                }
            }
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ 참가자: aliveT={alivePrisoners}, aliveCT={aliveGuards}, humanT={humanPrisoners}, humanCT={humanGuards}, botT={botPrisoners}, botCT={botGuards}");

        int maximumHumanGuards =
            _guardRatioManager?.GetMaximumGuardCount(humanPrisoners)
            ?? 0;

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ 간수 비율: humanCT={humanGuards}/{maximumHumanGuards}, humanT={humanPrisoners}, ratio=1:{_guardRatioManager?.PrisonersPerGuard ?? Config.PrisonersPerGuard}, adminBypass=true, botsExcluded=true");

        PrintRecentIncidents(commandInfo.ReplyToCommand, maxCount: 3);

        PrintGameRulesDiagnostics(commandInfo.ReplyToCommand);
    }

    private static void PrintHelp(Action<string> reply)
    {
        reply(" CS2.\x0BKR\x01｜ 플레이어: !지시, !5, !도움말");
        reply(" CS2.\x0BKR\x01｜ 관리: css_jb status | reset | order [cancel] | countdown | incidents");
        reply(" CS2.\x0BKR\x01｜ 자유시간: css_jb freeday <start|end|give|remove|list> [대상]");
        reply(" CS2.\x0BKR\x01｜ 반란자: css_jb rebel <add|remove|list> [대상]");
        reply(" CS2.\x0BKR\x01｜ 권한은 Jailbreak.json의 AdminPermissions에서 설정합니다.");
    }

    private void OnResetStateCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        ResetModeState("관리자 강제 초기화");

        commandInfo.ReplyToCommand(
            " CS2.\x0BKR\x01｜ 라운드 진행 상태를 제외한 Jailbreak 상태를 초기화했습니다.");
    }

    private void OnCountdownCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo,
        int seconds)
    {
        if (player is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        StartGuardCountdown(
            player,
            seconds,
            "시작",
            commandInfo.ReplyToCommand);
    }

    private void StartGuardCountdown(
        CCSPlayerController player,
        int seconds,
        string label,
        Action<string> reply)
    {
        bool adminOverride = HasAdminPermission(player);

        if (_freekillTimeManager?.IsActive == true)
        {
            reply(" CS2.\x0BKR\x01｜ 프리킬 시간 중에는 카운트다운을 시작할 수 없습니다.");
            return;
        }

        if (!EnsureGuardOrderRoundUsable(
                player,
                adminOverride,
                "countdown command") ||
            _guardOrderManager?.CanUse(
                player,
                adminOverride) != true)
        {
            reply(" CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 카운트다운을 사용할 수 있습니다.");
            return;
        }

        if (_countdownManager is null)
        {
            reply(" CS2.\x0BKR\x01｜ 카운트다운 관리자가 초기화되지 않았습니다.");
            return;
        }

        if (!_countdownManager.Start(
                player,
                seconds,
                label,
                out string error))
        {
            reply($" CS2.\x0BKR\x01｜ 카운트다운 실패: {error}");
            return;
        }

        AddIncident($"{player.PlayerName}님이 {seconds}초 카운트다운을 시작했습니다.");
    }

    private bool TryHandleTeamSwapChatCommand(
        CCSPlayerController player,
        string text)
    {
        string normalized = text.Trim();
        string lowered = normalized.ToLowerInvariant();

        if (lowered is "!수락" or "/수락" or "!accept" or "/accept")
        {
            AcceptTeamSwap(player, player.PrintToChat);
            return true;
        }

        if (lowered is "!거절" or "/거절" or "!deny" or "/deny")
        {
            DeclineTeamSwap(player, player.PrintToChat);
            return true;
        }

        string[] prefixes =
        [
            "!교환 ",
            "/교환 ",
            "!swap ",
            "/swap "
        ];

        foreach (string prefix in prefixes)
        {
            if (!normalized.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetQuery = normalized[prefix.Length..].Trim();
            RequestTeamSwap(player, targetQuery, player.PrintToChat);
            return true;
        }

        return false;
    }

    private void OnTeamSwapRequestCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        RequestTeamSwap(
            player,
            JoinArguments(commandInfo, 3),
            commandInfo.ReplyToCommand);
    }

    private void OnTeamSwapAcceptCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        AcceptTeamSwap(player, commandInfo.ReplyToCommand);
    }

    private void OnTeamSwapDeclineCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        DeclineTeamSwap(player, commandInfo.ReplyToCommand);
    }

    private void RequestTeamSwap(
        CCSPlayerController player,
        string targetQuery,
        Action<string> reply)
    {
        if (!TeamManager.IsHumanPlayer(player))
        {
            reply(" CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetQuery))
        {
            reply(" CS2.\x0BKR\x01｜ 사용법: css_jb team swap <대상>");
            return;
        }

        if (_teamSwapManager is null)
        {
            reply(" CS2.\x0BKR\x01｜ 팀교환 관리자가 초기화되지 않았습니다.");
            return;
        }

        CCSPlayerController? target =
            _teamSwapManager.FindTarget(targetQuery);

        if (target is null)
        {
            reply(" CS2.\x0BKR\x01｜ 대상 플레이어를 한 명으로 특정할 수 없습니다.");
            return;
        }

        if (!_teamSwapManager.RequestSwap(
                player,
                target,
                out string error))
        {
            reply($" CS2.\x0BKR\x01｜ 팀교환 요청 실패: {error}");
            return;
        }
    }

    private void AcceptTeamSwap(
        CCSPlayerController player,
        Action<string> reply)
    {
        if (_teamSwapManager is null)
        {
            reply(" CS2.\x0BKR\x01｜ 팀교환 관리자가 초기화되지 않았습니다.");
            return;
        }

        if (!_teamSwapManager.Accept(
                player,
                out string error))
        {
            reply($" CS2.\x0BKR\x01｜ 팀교환 수락 실패: {error}");
            return;
        }

        AddIncident($"{player.PlayerName}님이 팀교환 요청을 수락했습니다.");
    }

    private void DeclineTeamSwap(
        CCSPlayerController player,
        Action<string> reply)
    {
        if (_teamSwapManager is null)
        {
            reply(" CS2.\x0BKR\x01｜ 팀교환 관리자가 초기화되지 않았습니다.");
            return;
        }

        if (!_teamSwapManager.Decline(
                player,
                out string error))
        {
            reply($" CS2.\x0BKR\x01｜ 팀교환 거절 실패: {error}");
        }
    }

    private void OnIncidentsCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is not null &&
            (!player.IsValid || player.IsBot || player.IsHLTV))
        {
            return;
        }

        PrintRecentIncidents(commandInfo.ReplyToCommand, maxCount: 10);
    }

    private void PrintRecentIncidents(
        Action<string> reply,
        int maxCount)
    {
        IReadOnlyList<JailIncident> incidents =
            _incidentLogManager?.GetRecent()
            ?? [];

        if (incidents.Count == 0)
        {
            reply(" CS2.\x0BKR\x01｜ 최근 사건이 없습니다.");
            return;
        }

        foreach (JailIncident incident in incidents.Take(maxCount))
        {
            TimeSpan age = DateTimeOffset.UtcNow - incident.CreatedAt;
            int secondsAgo = Math.Max(0, (int)age.TotalSeconds);
            reply($" CS2.\x0BKR\x01｜ 최근 사건: {secondsAgo}초 전 - {incident.Text}");
        }
    }

    private void AddIncident(string text)
    {
        _incidentLogManager?.Add(text);
        _guardOrderManager?.DisplayHud();
    }

    private void OnMarkRebelCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        string targetQuery = GetRootTarget(commandInfo);

        if (string.IsNullOrWhiteSpace(targetQuery))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 사용법: css_jb rebel add <플레이어 이름 또는 SteamID64>");
            return;
        }

        CCSPlayerController? target = _rebelManager?.FindTarget(targetQuery);

        if (target is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상 플레이어를 한 명으로 특정할 수 없습니다.");
            return;
        }

        if (!AdminManager.CanPlayerTarget(player, target))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상의 관리자 면역도가 더 높아 변경할 수 없습니다.");
            return;
        }

        if (_rebelManager?.MarkRebel(
                target,
                "관리자 지정") != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 반란자로 지정할 수 없습니다. 대상이 살아 있는 죄수인지 또는 이미 반란자인지 확인하세요.");
            return;
        }

        _freedayManager?.RemovePersonalFreeday(
            target,
            "반란자 지정");

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ {target.PlayerName}을 반란자로 지정했습니다.");

        AddIncident($"{target.PlayerName}님이 반란자가 되었습니다. 사유: 관리자 지정");
    }

    private void OnUnmarkRebelCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        string targetQuery = GetRootTarget(commandInfo);

        if (string.IsNullOrWhiteSpace(targetQuery))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 사용법: css_jb rebel remove <플레이어 이름 또는 SteamID64>");
            return;
        }

        CCSPlayerController? target = _rebelManager?.FindTarget(targetQuery);

        if (target is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상 플레이어를 한 명으로 특정할 수 없습니다.");
            return;
        }

        if (!AdminManager.CanPlayerTarget(player, target))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 대상의 관리자 면역도가 더 높아 변경할 수 없습니다.");
            return;
        }

        if (_rebelManager?.UnmarkRebel(
                target,
                "관리자 해제") != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 해당 플레이어는 현재 반란자가 아닙니다.");
            return;
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ {target.PlayerName}의 반란자 상태를 해제했습니다.");

        AddIncident($"{target.PlayerName}님의 반란자 상태가 해제되었습니다.");
    }

    private void OnRebelListCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!HasAdminPermission(player))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        IReadOnlyList<string> rebels =
            _rebelManager?.GetRebelNames()
            ?? [];

        if (rebels.Count == 0)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 현재 반란자가 없습니다.");
            return;
        }

        commandInfo.ReplyToCommand(
            $" CS2.\x0BKR\x01｜ 반란자: {string.Join(", ", rebels)}");
    }

    private void OnGuardOrderMenuCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is null)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령은 게임 안에서 사용하세요.");
            return;
        }

        OpenGuardOrderMenu(
            player,
            commandInfo.ReplyToCommand);
    }

    private void OnCancelGuardOrderCommand(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (player is not null &&
            (!player.IsValid ||
             player.IsBot ||
             player.IsHLTV ||
             !HasAdminPermission(player)))
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 이 명령을 사용할 권한이 없습니다.");
            return;
        }

        if (_guardOrderManager?.Cancel(
                player,
                "관리자 취소",
                announce: true) != true)
        {
            commandInfo.ReplyToCommand(
                " CS2.\x0BKR\x01｜ 현재 진행 중인 간수 지시가 없습니다.");
            return;
        }

        _awaitingCustomOrderInput.Clear();

        commandInfo.ReplyToCommand(
            " CS2.\x0BKR\x01｜ 간수 지시를 취소했습니다.");
    }

    private void OpenGuardOrderMenu(
        CCSPlayerController player,
        Action<string>? reply = null)
    {
        bool adminOverride =
            HasAdminPermission(player);

        if (!EnsureGuardOrderRoundUsable(
                player,
                adminOverride,
                "guard order command"))
        {
            LogGameRulesDiagnostics(
                "guard order command recovery failed");

            string message =
                _guardOrderManager?.CanUse(
                    player,
                    adminOverride,
                    requireActiveRound: false) == true
                    ? " CS2.\x0BKR\x01｜ 아직 라운드가 시작되지 않았거나 웜업 중입니다."
                    : " CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 지시할 수 있습니다.";

            if (reply is not null)
            {
                reply(message);
            }
            else
            {
                player.PrintToChat(message);
            }

            return;
        }

        if (_guardOrderManager?.CanUse(
                player,
                adminOverride) != true)
        {
            const string message =
                " CS2.\x0BKR\x01｜ 간수 또는 권한이 있는 관리자만 지시할 수 있습니다.";

            if (reply is not null)
            {
                reply(message);
            }
            else
            {
                player.PrintToChat(message);
            }

            return;
        }

        _guardOrderManager.OpenLocationMenu(
            player,
            selectedPlayer =>
                EnsureGuardOrderRoundUsable(
                    selectedPlayer,
                    HasAdminPermission(selectedPlayer),
                    "guard order menu selection") &&
                _guardOrderManager?.CanUse(
                    selectedPlayer,
                    HasAdminPermission(selectedPlayer)) == true,
            () =>
            {
                if (_freedayManager?.EndGlobalFreeday(
                        "새 간수 지시") == true)
                {
                    StopAutoFreedayTimer();
                    StopFreedayHudTimer();
                }
            },
            selectedPlayer =>
            {
                _awaitingCustomOrderInput[selectedPlayer.SteamID] =
                    DateTimeOffset.UtcNow.AddSeconds(
                        Config.CustomOrderInputTimeoutSeconds);

                selectedPlayer.PrintToChat(
                    $" CS2.\x0BKR\x01｜ {Config.CustomOrderInputTimeoutSeconds}초 안에 다음 채팅 한 줄을 입력하세요. 취소: !취소");
            });
    }

    private bool EnsureGuardOrderRoundUsable(
        CCSPlayerController player,
        bool adminOverride,
        string reason)
    {
        bool tryRecoverResult =
            TryRecoverRoundState(reason);

        bool canUseWithoutRound =
            _guardOrderManager?.CanUse(
                player,
                adminOverride,
                requireActiveRound: false) == true;

        LogGuardOrderCommandAttempt(
            reason,
            player,
            tryRecoverResult,
            canUseWithoutRound);

        if (tryRecoverResult)
        {
            return true;
        }

        if (!canUseWithoutRound ||
            _roundManager is null)
        {
            return false;
        }

        string mapName = Server.MapName;

        _roundManager.RecoverActiveRound(
            mapName,
            $"{reason}; guard_order_actor_fallback; adminOverride={adminOverride}; player={player.PlayerName}; slot={player.Slot}; steamId={player.SteamID}; map='{mapName}'");

        Logger.LogWarning(
            "[CS2.KR] Guard order allowed by actor fallback. PluginInstance: {PluginInstance}, Reason: {Reason}, Player: {PlayerName}, Slot: {Slot}, SteamID: {SteamId}, Team: {Team}, PawnAlive: {PawnAlive}, Map: {Map}",
            _pluginInstanceId,
            reason,
            player.PlayerName,
            player.Slot,
            player.SteamID,
            player.Team,
            player.PawnIsAlive,
            mapName);

        return true;
    }

    private void LogGuardOrderCommandAttempt(
        string commandSource,
        CCSPlayerController player,
        bool? tryRecoverResult,
        bool? canUseResult)
    {
        Jailbreak.Models.JailRoundState? state =
            _roundManager?.State;

        Logger.LogInformation(
            "[CS2.KR] Guard order command attempt. PluginInstance: {PluginInstance}, CommandSource: {CommandSource}, PlayerName: {PlayerName}, Slot: {Slot}, SteamID: {SteamId}, IsBot: {IsBot}, IsHLTV: {IsHLTV}, IsValid: {IsValid}, Team: {Team}, PawnAlive: {PawnAlive}, Map: {Map}, RoundActive: {RoundActive}, RoundNumber: {RoundNumber}, LastRoundStateChangeReason: {LastRoundStateChangeReason}, LastRoundStateChangeEvent: {LastRoundStateChangeEvent}, TryRecoverResult: {TryRecoverResult}, CanUseResult: {CanUseResult}",
            _pluginInstanceId,
            commandSource,
            player.PlayerName,
            player.Slot,
            player.SteamID,
            player.IsBot,
            player.IsHLTV,
            player.IsValid,
            player.Team,
            player.PawnIsAlive,
            Server.MapName,
            state?.IsActive == true,
            state?.RoundNumber ?? 0,
            state?.LastChangeReason ?? "round manager unavailable",
            state?.LastChangeEventName ?? "round manager unavailable",
            tryRecoverResult?.ToString() ?? "not-run",
            canUseResult?.ToString() ?? "not-run");
    }

    private bool TryRecoverRoundState(
        string reason)
    {
        Logger.LogInformation(
            "[CS2.KR] TryRecoverRoundState enter. PluginInstance: {PluginInstance}, Reason: {Reason}, RoundActive: {RoundActive}, Round: {RoundNumber}, Map: {Map}",
            _pluginInstanceId,
            reason,
            _roundManager?.State.IsActive == true,
            _roundManager?.State.RoundNumber ?? 0,
            Server.MapName);

        if (_roundManager?.State.IsActive == true)
        {
            LogTryRecoverRoundStateResult(
                reason,
                true,
                "round manager already active");
            return true;
        }

        if (_roundManager is null)
        {
            LogTryRecoverRoundStateResult(
                reason,
                false,
                "round manager unavailable");
            return false;
        }

        string mapName = Server.MapName;

        if (string.IsNullOrWhiteSpace(mapName))
        {
            Logger.LogDebug(
                "[CS2.KR] Round recovery skipped because no map is loaded. PluginInstance: {PluginInstance}, Reason: {Reason}",
                _pluginInstanceId,
                reason);
            LogTryRecoverRoundStateResult(
                reason,
                false,
                "map name empty");
            return false;
        }

        try
        {
            CCSGameRulesProxy? proxy =
                Utilities
                    .FindAllEntitiesByDesignerName<CCSGameRulesProxy>(
                        "cs_gamerules")
                    .FirstOrDefault();

            CCSGameRules? gameRules = proxy?.GameRules;

            if (gameRules is not null)
            {
                bool restartScheduled =
                    gameRules.RestartRoundTime > Server.CurrentTime;

                bool gameRulesLookActive =
                    !gameRules.WarmupPeriod &&
                    !gameRules.GameRestart &&
                    !restartScheduled &&
                    gameRules.RoundStartTime > 0.0f;

                if (!gameRulesLookActive)
                {
                    LogTryRecoverRoundStateResult(
                        reason,
                        false,
                        $"game rules inactive; warmup={gameRules.WarmupPeriod}; restart={gameRules.GameRestart}; restartScheduled={restartScheduled}; roundStart={gameRules.RoundStartTime:F2}; serverTime={Server.CurrentTime:F2}");
                    return false;
                }

                double elapsedSeconds = Math.Max(
                    0.0,
                    Server.CurrentTime - gameRules.RoundStartTime);

                _roundManager.RecoverActiveRound(
                    mapName,
                    $"{reason}; warmup={gameRules.WarmupPeriod}; restart={gameRules.GameRestart}; roundStart={gameRules.RoundStartTime:F2}; restartAt={gameRules.RestartRoundTime:F2}; serverTime={Server.CurrentTime:F2}",
                    elapsedSeconds);

                LogTryRecoverRoundStateResult(
                    reason,
                    true,
                    "game rules active");
                return true;
            }

            Logger.LogDebug(
                "[CS2.KR] Round recovery skipped because cs_gamerules has no GameRules instance. PluginInstance: {PluginInstance}, Reason: {Reason}, Map: {Map}",
                _pluginInstanceId,
                reason,
                mapName);
            LogTryRecoverRoundStateResult(
                reason,
                false,
                "cs_gamerules missing GameRules");
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "[CS2.KR] Failed to inspect CS2 game rules while recovering round state. Reason: {Reason}",
                reason);
            LogTryRecoverRoundStateResult(
                reason,
                false,
                $"{exception.GetType().Name}: {exception.Message}");
        }

        return false;
    }

    private void LogTryRecoverRoundStateResult(
        string reason,
        bool result,
        string detail)
    {
        Logger.LogInformation(
            "[CS2.KR] TryRecoverRoundState result. PluginInstance: {PluginInstance}, Reason: {Reason}, Result: {Result}, Detail: {Detail}, RoundActive: {RoundActive}, Round: {RoundNumber}, LastRoundStateChangeReason: {LastRoundStateChangeReason}",
            _pluginInstanceId,
            reason,
            result,
            detail,
            _roundManager?.State.IsActive == true,
            _roundManager?.State.RoundNumber ?? 0,
            _roundManager?.State.LastChangeReason ?? "round manager unavailable");
    }

    private void PrintGameRulesDiagnostics(Action<string> reply)
    {
        foreach (string line in GetGameRulesDiagnosticLines())
        {
            reply($" CS2.\x0BKR\x01｜ 진단: {line}");
        }
    }

    private void LogGameRulesDiagnostics(string source)
    {
        foreach (string line in GetGameRulesDiagnosticLines())
        {
            Logger.LogInformation(
                "[CS2.KR] GameRules diagnostic. PluginInstance: {PluginInstance}, Source: {Source}, Values: {Values}",
                _pluginInstanceId,
                source,
                line);
        }
    }

    private static IReadOnlyList<string> GetGameRulesDiagnosticLines()
    {
        List<string> lines = new();
        string mapName = Server.MapName;
        var serverTime = Server.CurrentTime;

        lines.Add(
            $"map='{mapName}', mapNameEmpty={string.IsNullOrWhiteSpace(mapName)}, serverTime={serverTime:F2}");

        try
        {
            var proxies =
                Utilities
                    .FindAllEntitiesByDesignerName<CCSGameRulesProxy>(
                        "cs_gamerules")
                    .ToList();

            lines.Add($"gameRulesProxyCount={proxies.Count}");

            CCSGameRules? gameRules =
                proxies.FirstOrDefault()?.GameRules;

            if (gameRules is null)
            {
                lines.Add("gameRules=null");
                return lines;
            }

            bool worldReadyHint =
                !string.IsNullOrWhiteSpace(mapName) &&
                gameRules.LevelInitialized;

            lines.Add(
                $"worldReadyHint={worldReadyHint}, levelInitialized={gameRules.LevelInitialized}, hasMatchStarted={gameRules.HasMatchStarted}, firstConnected={gameRules.FirstConnected}, completeReset={gameRules.CompleteReset}, loadingRoundBackupData={gameRules.LoadingRoundBackupData}");

            lines.Add(
                $"warmup={gameRules.WarmupPeriod}, freeze={gameRules.FreezePeriod}, gameRestart={gameRules.GameRestart}, matchWaitingForResume={gameRules.MatchWaitingForResume}, technicalTimeout={gameRules.TechnicalTimeOut}");

            lines.Add(
                $"warmupStart={gameRules.WarmupPeriodStart:F2}, warmupEnd={gameRules.WarmupPeriodEnd:F2}, gameStart={gameRules.GameStartTime:F2}, matchStart={gameRules.MatchStartTime:F2}, roundStart={gameRules.RoundStartTime:F2}, restartRound={gameRules.RestartRoundTime:F2}, nextPhase={gameRules.TimeUntilNextPhaseStarts:F2}");

            lines.Add(
                $"roundTime={gameRules.RoundTime}, freezeTime={gameRules.FreezeTime}, gamePhase={gameRules.GamePhase}, totalRounds={gameRules.TotalRoundsPlayed}, phaseRounds={gameRules.RoundsPlayedThisPhase}, roundWinStatus={gameRules.RoundWinStatus}, roundWinReason={gameRules.RoundWinReason}");

            lines.Add(
                $"playersT={gameRules.NumTerrorist}, playersCT={gameRules.NumCT}, spawnableT={gameRules.NumSpawnableTerrorist}, spawnableCT={gameRules.NumSpawnableCT}, timeoutTActive={gameRules.TerroristTimeOutActive}, timeoutCTActive={gameRules.CTTimeOutActive}");
        }
        catch (Exception exception)
        {
            lines.Add(
                $"diagnosticError={exception.GetType().Name}: {exception.Message}");
        }

        return lines;
    }

    private bool HasAdminPermission(CCSPlayerController? player)
    {
        if (player is null)
        {
            return true;
        }

        if (!player.IsValid ||
            player.IsBot ||
            player.IsHLTV ||
            player.SteamID == 0)
        {
            return false;
        }

        return Config.AdminPermissions.Any(permission =>
            AdminManager.PlayerHasPermissions(player, permission));
    }

    private void ApplyRadarPolicy(string reason)
    {
        if (!Config.DisableRadar)
        {
            Logger.LogInformation(
                "[CS2.KR] Radar policy skipped. Reason: {Reason}, DisableRadar: false",
                reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.DisableRadarCommand))
        {
            Logger.LogWarning(
                "[CS2.KR] Radar policy skipped because DisableRadarCommand is empty. Reason: {Reason}",
                reason);
            return;
        }

        try
        {
            Server.ExecuteCommand(Config.DisableRadarCommand);

            Logger.LogInformation(
                "[CS2.KR] Radar policy applied. Reason: {Reason}, Command: {Command}",
                reason,
                Config.DisableRadarCommand);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "[CS2.KR] Radar policy failed. Reason: {Reason}, Command: {Command}",
                reason,
                Config.DisableRadarCommand);
        }
    }

    private void ResetModeState(string reason)
    {
        StopAutoFreedayTimer();
        StopFreedayHudTimer();
        _rebelManager?.RestoreAllRebels();
        _freedayManager?.EndGlobalFreeday(reason);
        _freedayManager?.ClearPersonalFreedays(
            reason,
            announce: false);
        _freekillTimeManager?.Reset();        _countdownManager?.Stop();
        _guardOrderManager?.Cancel(null, reason, announce: false);
        _guardOrderManager?.Clear();
        _awaitingCustomOrderInput.Clear();
        _teamSwapManager?.Clear();
        _incidentLogManager?.Clear();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;
        _playerStateManager?.ResetRoundStates();

        Logger.LogWarning(
            "[CS2.KR] Mode state reset. PluginInstance: {PluginInstance}, Reason: {Reason}, RoundActive: {RoundActive}, Round: {RoundNumber}",
            _pluginInstanceId,
            reason,
            _roundManager?.State.IsActive == true,
            _roundManager?.State.RoundNumber ?? 0);
    }

    private void StartFreedayHudTimer()
    {
        StopFreedayHudTimer();

        _freedayManager?.DisplayGlobalFreedayHud();

        _freedayHudTimer = AddTimer(
            1.0f,
            () =>
            {
                if (_roundManager?.State.IsFreedayRound != true)
                {
                    StopFreedayHudTimer();
                    return;
                }

                _freedayManager?.DisplayGlobalFreedayHud();
            },
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StartAutomaticFreedayIfConfigured()
    {
        StopAutoFreedayTimer();

        if (!Config.AutoFreedayEnabled ||
            Config.AutoFreedayDurationSeconds <= 0 ||
            _roundManager?.State.IsActive != true ||
            _freekillTimeManager?.IsActive == true)
        {
            return;
        }

        _guardOrderManager?.Cancel(
            null,
            "자동 자유시간 시작",
            announce: false);
        _freekillTimeManager?.Reset();

        if (_freedayManager?.StartGlobalFreeday() != true)
        {
            return;
        }

        _freedayManager?.ClearPersonalFreedays(
            "자동 자유시간 시작",
            announce: false);
        StartFreedayHudTimer();
        AddIncident(
            $"자동 자유시간이 시작되었습니다. {Config.AutoFreedayDurationSeconds}초");

        _autoFreedayTimer = AddTimer(
            Config.AutoFreedayDurationSeconds,
            EndAutomaticFreeday,
            TimerFlags.STOP_ON_MAPCHANGE);

        Logger.LogInformation(
            "[CS2.KR] Automatic freeday started. DurationSeconds: {DurationSeconds}, Round: {RoundNumber}",
            Config.AutoFreedayDurationSeconds,
            _roundManager.State.RoundNumber);
    }

    private void EndAutomaticFreeday()
    {
        _autoFreedayTimer = null;

        if (_roundManager?.State.IsActive != true ||
            _freekillTimeManager?.IsActive == true ||
            _freedayManager?.EndGlobalFreeday("자동 종료") != true)
        {
            return;
        }

        StopFreedayHudTimer();
        CaptureFreekillBaseline();
        AddIncident("자동 자유시간이 종료되었습니다.");

        Logger.LogInformation(
            "[CS2.KR] Automatic freeday ended. Round: {RoundNumber}",
            _roundManager.State.RoundNumber);
    }

    private void StopAutoFreedayTimer()
    {
        _autoFreedayTimer?.Kill();
        _autoFreedayTimer = null;
    }

    private void StopFreedayHudTimer()
    {
        _freedayHudTimer?.Kill();
        _freedayHudTimer = null;
    }

    private void StartRebelColorTimer()
    {
        StopRebelColorTimer();

        _rebelColorTimer = AddTimer(
            1.0f,
            () => _rebelManager?.RefreshRebelColors(),
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StopRebelColorTimer()
    {
        _rebelColorTimer?.Kill();
        _rebelColorTimer = null;
    }

    private HookResult OnJoinTeam(
        CCSPlayerController? player,
        CommandInfo commandInfo)
    {
        if (!CanUseCommand(player))
        {
            return HookResult.Continue;
        }

        CCSPlayerController trackedPlayer = player!;
        string requestedTeamArgument = commandInfo.GetArg(1);

        if (!int.TryParse(requestedTeamArgument, out int requestedTeam) ||
            requestedTeam != (int)CsTeam.CounterTerrorist)
        {
            return HookResult.Continue;
        }

        if (HasAdminPermission(trackedPlayer))
        {
            GuardRatioSnapshot? adminSnapshot =
                _guardRatioManager?.GetSnapshot(trackedPlayer);

            Logger.LogInformation(
                "[CS2.KR] Guard join allowed by admin override. Player: {PlayerName}, SteamID: {SteamId}, Prisoners: {Prisoners}, Guards: {Guards}, ProjectedPrisoners: {ProjectedPrisoners}, ProjectedGuards: {ProjectedGuards}, MaximumGuards: {MaximumGuards}, PrisonersPerGuard: {PrisonersPerGuard}",
                trackedPlayer.PlayerName,
                trackedPlayer.SteamID,
                adminSnapshot?.PrisonerCount ?? 0,
                adminSnapshot?.GuardCount ?? 0,
                adminSnapshot?.ProjectedPrisonerCount ?? 0,
                adminSnapshot?.ProjectedGuardCount ?? 0,
                adminSnapshot?.MaximumGuardCount ?? 0,
                adminSnapshot?.PrisonersPerGuard ?? Config.PrisonersPerGuard);

            return HookResult.Continue;
        }

        GuardRatioSnapshot? snapshot =
            _guardRatioManager?.GetSnapshot(trackedPlayer);

        if (snapshot?.CanJoinGuard == true)
        {
            Logger.LogInformation(
                "[CS2.KR] Guard join allowed by ratio. Player: {PlayerName}, SteamID: {SteamId}, Prisoners: {Prisoners}, Guards: {Guards}, ProjectedPrisoners: {ProjectedPrisoners}, ProjectedGuards: {ProjectedGuards}, MaximumGuards: {MaximumGuards}, PrisonersPerGuard: {PrisonersPerGuard}, PlayerAlreadyGuard: {PlayerAlreadyGuard}",
                trackedPlayer.PlayerName,
                trackedPlayer.SteamID,
                snapshot.PrisonerCount,
                snapshot.GuardCount,
                snapshot.ProjectedPrisonerCount,
                snapshot.ProjectedGuardCount,
                snapshot.MaximumGuardCount,
                snapshot.PrisonersPerGuard,
                snapshot.PlayerAlreadyGuard);

            return HookResult.Continue;
        }

        commandInfo.ReplyToCommand(
            " CS2.\x0BKR\x01｜ 현재 죄수 인원에 비해 간수가 너무 많아 CT팀에 참가할 수 없습니다.");

        Logger.LogInformation(
            "[CS2.KR] Guard join blocked. Player: {PlayerName}, SteamID: {SteamId}, Prisoners: {Prisoners}, Guards: {Guards}, ProjectedPrisoners: {ProjectedPrisoners}, ProjectedGuards: {ProjectedGuards}, MaximumGuards: {MaximumGuards}, PrisonersPerGuard: {PrisonersPerGuard}, SnapshotAvailable: {SnapshotAvailable}",
            trackedPlayer.PlayerName,
            trackedPlayer.SteamID,
            snapshot?.PrisonerCount ?? 0,
            snapshot?.GuardCount ?? 0,
            snapshot?.ProjectedPrisonerCount ?? 0,
            snapshot?.ProjectedGuardCount ?? 0,
            snapshot?.MaximumGuardCount ?? 0,
            snapshot?.PrisonersPerGuard ?? Config.PrisonersPerGuard,
            snapshot is not null);

        return HookResult.Handled;
    }

    private HookResult OnRoundPrestart(
        EventRoundPrestart @event,
        GameEventInfo info)
    {
        Logger.LogInformation(
            "[CS2.KR] Event received: round_prestart. PluginInstance: {PluginInstance}, PluginRoundActive: {RoundActive}, Map: {Map}",
            _pluginInstanceId,
            _roundManager?.State.IsActive == true,
            Server.MapName);

        LogGameRulesDiagnostics("event round_prestart");

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(
        EventRoundStart @event,
        GameEventInfo info)
    {
        Logger.LogInformation(
            "[CS2.KR] Event received: round_start. PluginInstance: {PluginInstance}, PluginRoundActiveBefore: {RoundActive}, Map: {Map}",
            _pluginInstanceId,
            _roundManager?.State.IsActive == true,
            Server.MapName);

        LogGameRulesDiagnostics("event round_start before state update");

        StopFreedayHudTimer();
        _guardOrderManager?.Clear();
        _awaitingCustomOrderInput.Clear();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;
        _teamSwapManager?.Clear();
        _rebelManager?.RestoreAllRebels();
        _freedayManager?.ClearPersonalFreedays(
            "라운드 시작",
            announce: false);
        _freekillTimeManager?.Reset();        _countdownManager?.Stop();
        _incidentLogManager?.Clear();
        _playerStateManager?.ResetRoundStates();
        _roundManager?.HandleRoundStart();
        CaptureFreekillBaseline();
        StartAutomaticFreedayIfConfigured();
        _guardOrderManager?.DisplayHud();
        ScheduleFreekillEvaluation("round start");

        Logger.LogInformation(
            "[CS2.KR] Event handled: round_start. PluginInstance: {PluginInstance}, PluginRoundActiveAfter: {RoundActive}, Round: {RoundNumber}",
            _pluginInstanceId,
            _roundManager?.State.IsActive == true,
            _roundManager?.State.RoundNumber ?? 0);

        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(
        EventRoundEnd @event,
        GameEventInfo info)
    {
        Logger.LogInformation(
            "[CS2.KR] Event received: round_end. PluginInstance: {PluginInstance}, PluginRoundActiveBefore: {RoundActive}, Round: {RoundNumber}, Map: {Map}",
            _pluginInstanceId,
            _roundManager?.State.IsActive == true,
            _roundManager?.State.RoundNumber ?? 0,
            Server.MapName);

        LogGameRulesDiagnostics("event round_end before state update");

        StopAutoFreedayTimer();
        _awaitingCustomOrderInput.Clear();
        _teamSwapManager?.Clear();
        _rebelManager?.RestoreAllRebels();
        _freedayManager?.EndGlobalFreeday("라운드 종료");
        _freedayManager?.ClearPersonalFreedays(
            "라운드 종료",
            announce: false);
        _freekillTimeManager?.Reset();        _countdownManager?.Stop();
        _guardOrderManager?.Cancel(null, "라운드 종료", announce: false);
        StopFreedayHudTimer();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;
        _roundManager?.HandleRoundEnd();
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(
        EventPlayerHurt @event,
        GameEventInfo info)
    {
        _freedayManager?.HandlePlayerDamage(
            @event.Attacker,
            @event.Userid,
            @event.DmgHealth);

        if (_rebelManager?.HandleGuardDamage(
            @event.Attacker,
            @event.Userid,
            @event.DmgHealth) == true &&
            @event.Attacker is not null)
        {
            AddIncident($"{@event.Attacker.PlayerName}님이 반란자가 되었습니다.");
        }

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(
        EventPlayerDeath @event,
        GameEventInfo info)
    {
        if (CanUseCommand(@event.Userid))
        {
            _awaitingCustomOrderInput.Remove(@event.Userid!.SteamID);
        }

        _rebelManager?.RestoreRebel(@event.Userid);
        ScheduleFreekillEvaluation("player death");

        return HookResult.Continue;
    }

    private HookResult OnPlayerTeam(
        EventPlayerTeam @event,
        GameEventInfo info)
    {
        CCSPlayerController? player = @event.Userid;

        if (player is null ||
            !player.IsValid ||
            player.IsHLTV)
        {
            return HookResult.Continue;
        }

        CCSPlayerController trackedPlayer = player;

        _rebelManager?.RestoreRebel(trackedPlayer);
        if (CanUseCommand(trackedPlayer))
        {
            _awaitingCustomOrderInput.Remove(trackedPlayer.SteamID);
        }

        _playerStateManager?.GetOrCreate(trackedPlayer)?.ResetRoundState();

        if (PlayerStateManager.TryGetStateKey(
                trackedPlayer,
                out ulong stateKey))
        {
            if (CanTrackGameplay(trackedPlayer))
            {
                _stateKeysBySlot[trackedPlayer.Slot] = stateKey;
            }
            else
            {
                _stateKeysBySlot.Remove(trackedPlayer.Slot);
                _playerStateManager?.RemoveStateKey(stateKey);
            }
        }

        Logger.LogInformation(
            "[CS2.KR] Player changed team. Player: {PlayerName}, SteamID: {SteamId}, OldTeam: {OldTeam}, NewTeam: {NewTeam}",
            trackedPlayer.PlayerName,
            trackedPlayer.SteamID,
            @event.Oldteam,
            @event.Team);

        ScheduleFreekillEvaluation("player team");

        return HookResult.Continue;
    }

    private void OnMapStart(string mapName)
    {
        Logger.LogInformation(
            "[CS2.KR] Listener received: map_start. PluginInstance: {PluginInstance}, Map argument: {MapArgument}, Server.MapName: {ServerMapName}",
            _pluginInstanceId,
            mapName,
            Server.MapName);

        StopAutoFreedayTimer();
        StopFreedayHudTimer();
        _rebelManager?.RestoreAllRebels();
        _countdownManager?.Stop();
        _incidentLogManager?.Clear();
        _freekillTimeManager?.Reset();        _playerStateManager?.Clear();
        _guardOrderManager?.Clear();
        _awaitingCustomOrderInput.Clear();
        _teamSwapManager?.Clear();
        _stateKeysBySlot.Clear();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;
        TrackConnectedPlayers();
        StartRebelColorTimer();
        _roundManager?.HandleMapStart(mapName);
        ApplyRadarPolicy("map start");

        AddTimer(
            1.0f,
            () => LogGameRulesDiagnostics(
                "map_start +1.0s"),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OnMapEnd()
    {
        Logger.LogInformation(
            "[CS2.KR] Listener received: map_end. PluginInstance: {PluginInstance}, Server.MapName: {Map}",
            _pluginInstanceId,
            Server.MapName);

        // map_end 시점엔 맵/엔티티가 이미 언로드되는 중이라(MapName=null) 엔티티
        // 조회가 네이티브 크래시(try/catch로도 못 잡음)를 일으켜 서버가 죽었습니다.
        // GameRules 진단, RestoreAllRebels/GuardOrder HUD 정리(GetPlayers), 비콘
        // 엔티티 제거를 모두
        // 제거하고, 타이머 정지와 메모리 상태 리셋만 수행합니다. pawn 색·HUD 복원은
        // 맵이 끝나는 시점에 의미가 없고 다음 맵에서 상태가 새로 초기화됩니다.
        StopAutoFreedayTimer();
        StopFreedayHudTimer();
        _freekillTimeManager?.Reset();        _countdownManager?.Stop();
        _guardOrderManager?.ResetForMapEnd();
        _awaitingCustomOrderInput.Clear();
        _teamSwapManager?.Clear();
        _roundManager?.HandleMapEnd();
        _playerStateManager?.Clear();
        _stateKeysBySlot.Clear();
        _pendingFreekillEvaluationReasons.Clear();
        _freekillEvaluationPending = false;
    }

    private void OnClientPutInServer(int playerSlot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

        if (!CanTrackGameplay(player))
        {
            return;
        }

        CCSPlayerController trackedPlayer = player!;

        _playerStateManager?.GetOrCreate(trackedPlayer);
        if (PlayerStateManager.TryGetStateKey(
                trackedPlayer,
                out ulong stateKey))
        {
            _stateKeysBySlot[playerSlot] = stateKey;
        }

        Logger.LogInformation(
            "[CS2.KR] Player connected. Player: {PlayerName}, SteamID: {SteamId}, IsBot: {IsBot}, Slot: {Slot}",
            trackedPlayer.PlayerName,
            trackedPlayer.SteamID,
            trackedPlayer.IsBot,
            playerSlot);
    }

    private void OnClientDisconnect(int playerSlot)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSlot(playerSlot);

        _rebelManager?.RestoreRebel(player);

        if (!_stateKeysBySlot.Remove(playerSlot, out ulong stateKey))
        {
            return;
        }

        ulong steamId = player?.SteamID ?? 0;

        if (steamId == 0 &&
            PlayerStateManager.TryGetSteamIdFromStateKey(
                stateKey,
                out ulong steamIdFromStateKey))
        {
            steamId = steamIdFromStateKey;
        }

        if (steamId != 0)
        {
            _awaitingCustomOrderInput.Remove(steamId);
            _teamSwapManager?.RemovePlayer(steamId);
        }

        _playerStateManager?.RemoveStateKey(stateKey);
        ScheduleFreekillEvaluation("client disconnect");

        Logger.LogInformation(
            "[CS2.KR] Player disconnected. SteamID: {SteamId}, StateKey: {StateKey}, Slot: {Slot}",
            steamId,
            stateKey,
            playerSlot);
    }

    private void TrackConnectedPlayers()
    {
        if (_playerStateManager is null)
        {
            return;
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!CanTrackGameplay(player))
            {
                continue;
            }

            _playerStateManager.GetOrCreate(player);
            if (PlayerStateManager.TryGetStateKey(
                    player,
                    out ulong stateKey))
            {
                _stateKeysBySlot[player.Slot] = stateKey;
            }
        }
    }

    private static bool CanUseCommand(CCSPlayerController? player)
    {
        return TeamManager.CanUseCommand(player);
    }

    private static bool CanTrackGameplay(CCSPlayerController? player)
    {
        return TeamManager.IsGameplayParticipant(player);
    }

    private void CaptureFreekillBaseline()
    {
        if (_freekillTimeManager is null ||
            _roundManager?.State.IsActive != true ||
            _roundManager.State.IsFreedayRound)
        {
            return;
        }

        JailAliveCounts counts = GetAliveCounts();
        _freekillTimeManager.CaptureBaseline(counts.AliveGuards);
    }

    private void ScheduleFreekillEvaluation(string reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
        {
            _pendingFreekillEvaluationReasons.Add(reason.Trim());
        }

        if (_freekillEvaluationPending)
        {
            return;
        }

        _freekillEvaluationPending = true;

        AddTimer(
            0.1f,
            () =>
            {
                string joinedReasons = _pendingFreekillEvaluationReasons.Count == 0
                    ? reason
                    : string.Join(", ", _pendingFreekillEvaluationReasons);

                _pendingFreekillEvaluationReasons.Clear();
                _freekillEvaluationPending = false;
                EvaluateFreekillTime(joinedReasons);
            },
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void EvaluateFreekillTime(string reason)
    {
        if (_freekillTimeManager is null ||
            _roundManager?.State.IsActive != true)
        {
            return;
        }

        JailAliveCounts counts = GetAliveCounts();

        if (_freekillTimeManager.Evaluate(
                counts.AliveGuards,
                counts.AlivePrisoners,
                out bool ended))
        {
            string guardText = string.IsNullOrWhiteSpace(counts.LastGuardName)
                ? "마지막 간수"
                : $"마지막 간수: {counts.LastGuardName}";

            StopAutoFreedayTimer();
            if (_freedayManager?.EndGlobalFreeday(
                    "프리킬 시간 시작") == true)
            {
                StopFreedayHudTimer();
            }

            _guardOrderManager?.SetPriorityHud("프리킬 시간 입니다!");
            BroadcastChat($" CS2.\x0BKR\x01｜ 프리킬 시간입니다. {guardText}");
            PrintCenterAll($"CS2.KR｜\n프리킬 시간입니다\n{guardText}");
            AddIncident($"프리킬 시간이 시작되었습니다. {guardText}");

            Logger.LogInformation(
                "[CS2.KR] Freekill time started. Reason: {Reason}, AliveGuards: {AliveGuards}, AlivePrisoners: {AlivePrisoners}, LastGuard: {LastGuard}",
                reason,
                counts.AliveGuards,
                counts.AlivePrisoners,
                counts.LastGuardName);
        }
        else if (ended)
        {
            _guardOrderManager?.ClearPriorityHud();
            AddIncident("프리킬 시간이 종료되었습니다.");

            Logger.LogInformation(
                "[CS2.KR] Freekill time ended. Reason: {Reason}, AliveGuards: {AliveGuards}, AlivePrisoners: {AlivePrisoners}",
                reason,
                counts.AliveGuards,
                counts.AlivePrisoners);
        }
    }

    private static JailAliveCounts GetAliveCounts()
    {
        int alivePrisoners = 0;
        int aliveGuards = 0;
        string? lastGuardName = null;

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !player.PawnIsAlive)
            {
                continue;
            }

            if (TeamManager.IsPrisoner(player))
            {
                alivePrisoners++;
            }
            else if (TeamManager.IsGuard(player))
            {
                aliveGuards++;
                lastGuardName = player.PlayerName;
            }
        }

        return new JailAliveCounts(
            alivePrisoners,
            aliveGuards,
            lastGuardName);
    }

    private static void BroadcastChat(string message)
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (TeamManager.IsHumanPlayer(player))
            {
                player.PrintToChat(message);
            }
        }
    }

    private static void PrintCenterAll(string message)
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (TeamManager.IsHumanPlayer(player))
            {
                player.PrintToCenter(message);
            }
        }
    }

    private sealed record JailAliveCounts(
        int AlivePrisoners,
        int AliveGuards,
        string? LastGuardName);

}
