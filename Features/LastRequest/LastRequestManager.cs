using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Extensions;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Jailbreak.Core;
using Jailbreak.Features.Teams;
using Jailbreak.Models;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.LastRequest;

public enum LastRequestGame
{
    None,
    Knife,
    Pistol,
    NoScope
}

public sealed class LastRequestManager
{
    private readonly BasePlugin _plugin;
    private readonly PlayerStateManager _playerStateManager;
    private readonly RoundManager _roundManager;
    private readonly ILogger _logger;
    private readonly bool _botsBlockLastRequest;

    private ulong _currentLastRequestSteamId;
    private ulong _opponentSteamId;
    private int _currentLastRequestSlot = -1;
    private int _opponentSlot = -1;
    private LastRequestGame _activeGame;
    private bool _closedForRound;
    private readonly Dictionary<ulong, DateTimeOffset> _lastNoScopeWarningAt = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _loadoutTimer;

    public LastRequestManager(
        BasePlugin plugin,
        PlayerStateManager playerStateManager,
        RoundManager roundManager,
        bool botsBlockLastRequest,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(playerStateManager);
        ArgumentNullException.ThrowIfNull(roundManager);
        ArgumentNullException.ThrowIfNull(logger);

        _plugin = plugin;
        _playerStateManager = playerStateManager;
        _roundManager = roundManager;
        _botsBlockLastRequest = botsBlockLastRequest;
        _logger = logger;
    }

    public ulong CurrentLastRequestSteamId => _currentLastRequestSteamId;

    public LastRequestGame ActiveGame => _activeGame;

    public bool HasActiveGame => _activeGame != LastRequestGame.None;

    public bool IsParticipant(CCSPlayerController? player)
    {
        return IsActiveParticipant(player);
    }

    public bool ShouldBlockDamage(
        CCSPlayerController? attacker,
        CCSPlayerController? victim,
        out string reason)
    {
        reason = string.Empty;

        if (_activeGame == LastRequestGame.None)
        {
            return false;
        }

        bool attackerParticipates = IsActiveParticipant(attacker);
        bool victimParticipates = IsActiveParticipant(victim);

        if (attackerParticipates &&
            victimParticipates &&
            !IsAttackingWithAllowedWeapon(attacker!, out string? weaponName))
        {
            reason = $"lr disallowed weapon damage ({weaponName ?? "unknown"})";
            return true;
        }

        if (_activeGame == LastRequestGame.NoScope &&
            attackerParticipates &&
            attacker is not null &&
            (attacker.Buttons & PlayerButtons.Zoom) != 0)
        {
            reason = "no-scope zoom damage";
            return true;
        }

        if (attackerParticipates && victimParticipates)
        {
            return false;
        }

        if (attackerParticipates || victimParticipates)
        {
            reason = attackerParticipates
                ? "lr participant attacked non-participant"
                : "non-participant attacked lr participant";
            return true;
        }

        return false;
    }

    private bool IsAttackingWithAllowedWeapon(
        CCSPlayerController attacker,
        out string? weaponName)
    {
        try
        {
            weaponName = GetActiveWeaponName(attacker);
        }
        catch (Exception exception)
        {
            weaponName = null;

            _logger.LogWarning(
                exception,
                "[Jailbreak] LR active weapon lookup failed. Attacker: {Attacker}, SteamID: {SteamID}, Slot: {Slot}, Game: {Game}",
                attacker.PlayerName,
                attacker.SteamID,
                attacker.Slot,
                _activeGame);

            return true;
        }

        if (string.IsNullOrWhiteSpace(weaponName))
        {
            return true;
        }

        return IsAllowedWeapon(_activeGame, weaponName);
    }

    public string GetStatusText()
    {
        if (_activeGame != LastRequestGame.None)
        {
            return $"진행 중: {GetGameLabel(_activeGame)} / prisoner={_currentLastRequestSteamId}@{_currentLastRequestSlot} / opponent={_opponentSteamId}@{_opponentSlot}";
        }

        if (_currentLastRequestSteamId != 0 ||
            _currentLastRequestSlot >= 0)
        {
            return $"대기 중: 마지막 죄수 메뉴 사용 가능 / prisoner={_currentLastRequestSteamId}@{_currentLastRequestSlot}";
        }

        if (_roundManager.State.IsFreedayRound)
        {
            return "비활성: 전체 자유시간 진행 중";
        }

        if (_closedForRound)
        {
            return "종료됨: 이번 라운드 LR 완료 또는 취소";
        }

        return "없음";
    }

    public string GetDiagnosticText()
    {
        int aliveHumanPrisoners = 0;
        int aliveGameplayPrisoners = 0;
        int aliveHumanGuards = 0;
        int aliveBotGuards = 0;

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !player.PawnIsAlive)
            {
                continue;
            }

            if (TeamManager.IsPrisoner(player))
            {
                aliveGameplayPrisoners++;

                if (IsValidHumanPlayer(player))
                {
                    aliveHumanPrisoners++;
                }
            }
            else if (TeamManager.IsGuard(player))
            {
                if (IsValidHumanPlayer(player))
                {
                    aliveHumanGuards++;
                }
                else if (player.IsBot)
                {
                    aliveBotGuards++;
                }
            }
        }

        return
            $"active={_roundManager.State.IsActive}, freeday={_roundManager.State.IsFreedayRound}, closed={_closedForRound}, botsBlockLR={_botsBlockLastRequest}, " +
            $"aliveHumanT={aliveHumanPrisoners}, aliveGameplayT={aliveGameplayPrisoners}, aliveHumanCT={aliveHumanGuards}, aliveBotCT={aliveBotGuards}";
    }

    public void OpenMenu(CCSPlayerController player)
    {
        EnsureLastRequestCandidate(player, "open menu");

        if (!CanOpenMenu(player, out string error))
        {
            player.PrintToChat($"[Jailbreak] LR 메뉴를 열 수 없습니다. {error}");
            return;
        }

        CenterHtmlMenu menu = new("LR - 게임 선택", _plugin) { ExitButton = true };

        menu.AddMenuOption(
            "칼전",
            (selectedPlayer, _) =>
                OpenOpponentMenu(
                    selectedPlayer,
                    LastRequestGame.Knife));

        menu.AddMenuOption(
            "권총전",
            (selectedPlayer, _) =>
                OpenOpponentMenu(
                    selectedPlayer,
                    LastRequestGame.Pistol));

        menu.AddMenuOption(
            "노스코프전",
            (selectedPlayer, _) =>
                OpenOpponentMenu(
                    selectedPlayer,
                    LastRequestGame.NoScope));

        menu.Open(player);
    }

    public void Evaluate()
    {
        if (!_roundManager.State.IsActive ||
            _roundManager.State.IsFreedayRound ||
            _closedForRound)
        {
            ClearActiveState();
            return;
        }

        List<CCSPlayerController> aliveOpponents = GetAliveGuards().ToList();

        if (aliveOpponents.Count == 0)
        {
            _logger.LogInformation(
                "[Jailbreak] LR evaluation skipped: no alive guards. Round: {RoundNumber}",
                _roundManager.State.RoundNumber);
            ClearActiveState();
            return;
        }

        List<CCSPlayerController> alivePrisoners = Utilities.GetPlayers()
            .Where(IsAliveLastRequestBlockingPrisoner)
            .ToList();

        if (alivePrisoners.Count != 1)
        {
            _logger.LogInformation(
                "[Jailbreak] LR evaluation skipped: alive LR-blocking prisoner count is {AlivePrisoners}. AliveGuards: {AliveGuards}, BotsBlockLastRequest: {BotsBlockLastRequest}, Round: {RoundNumber}",
                alivePrisoners.Count,
                aliveOpponents.Count,
                _botsBlockLastRequest,
                _roundManager.State.RoundNumber);
            ClearActiveState();
            return;
        }

        CCSPlayerController prisoner = alivePrisoners[0];

        if (!IsValidHumanPlayer(prisoner))
        {
            ClearActiveState();
            return;
        }

        if (_currentLastRequestSteamId == prisoner.SteamID)
        {
            return;
        }

        ClearActiveState();

        JailPlayerState? state =
            _playerStateManager.GetOrCreate(prisoner);

        if (state is null)
        {
            return;
        }

        state.IsInLastRequest = true;
        _currentLastRequestSteamId = prisoner.SteamID;
        _currentLastRequestSlot = prisoner.Slot;

        BroadcastChat(
            $"[Jailbreak] 마지막 죄수 {prisoner.PlayerName}의 LR이 가능합니다.");

        prisoner.PrintToCenter("[Jailbreak]\n마지막 죄수 - !lr");

        _logger.LogInformation(
            "[Jailbreak] Last prisoner detected. Player: {PlayerName}, SteamID: {SteamId}, Round: {RoundNumber}",
            prisoner.PlayerName,
            prisoner.SteamID,
            _roundManager.State.RoundNumber);
    }

    public void ResetRound()
    {
        ClearActiveState();
        _closedForRound = false;
    }

    // map_end 전용: 맵/엔티티가 언로드되는 중이라 GetPlayers 조회가 네이티브
    // 크래시를 내므로, 엔티티를 만지지 않고 내부 LR 상태와 타이머만 리셋합니다.
    // 플레이어별 IsInLastRequest는 이어지는 PlayerStateManager.Clear()가 지웁니다.
    public void ResetForMapEnd()
    {
        _currentLastRequestSteamId = 0;
        _opponentSteamId = 0;
        _currentLastRequestSlot = -1;
        _opponentSlot = -1;
        _activeGame = LastRequestGame.None;
        _lastNoScopeWarningAt.Clear();
        StopLoadoutTimer();
        _closedForRound = false;
    }

    public void Clear()
    {
        ClearActiveState();
        _closedForRound = true;
    }

    public bool Cancel(
        string reason,
        bool announce)
    {
        if (_currentLastRequestSteamId == 0 &&
            _opponentSteamId == 0 &&
            _activeGame == LastRequestGame.None)
        {
            return false;
        }

        string gameLabel = GetGameLabel(_activeGame);

        if (announce)
        {
            BroadcastChat(
                $"[Jailbreak] LR {gameLabel}이 취소되었습니다. 사유: {reason}");
        }

        _logger.LogInformation(
            "[Jailbreak] Last request cancelled. Game: {Game}, Reason: {Reason}",
            _activeGame,
            reason);

        Clear();
        return true;
    }

    private void ClearActiveState()
    {
        if (_currentLastRequestSteamId == 0 &&
            _opponentSteamId == 0 &&
            _currentLastRequestSlot < 0 &&
            _opponentSlot < 0 &&
            _activeGame == LastRequestGame.None)
        {
            return;
        }

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!IsValidHumanPlayer(player) ||
                !_playerStateManager.TryGet(
                    player.SteamID,
                    out JailPlayerState? state) ||
                state is null)
            {
                continue;
            }

            state.IsInLastRequest = false;
        }

        _currentLastRequestSteamId = 0;
        _opponentSteamId = 0;
        _currentLastRequestSlot = -1;
        _opponentSlot = -1;
        _activeGame = LastRequestGame.None;
        _lastNoScopeWarningAt.Clear();
        StopLoadoutTimer();
    }

    public void HandlePlayerDeath(CCSPlayerController? victim)
    {
        if (!TeamManager.IsGameplayParticipant(victim))
        {
            return;
        }

        if (_activeGame == LastRequestGame.None ||
            !IsActiveParticipant(victim))
        {
            ClearPlayer(
                victim,
                "사망",
                announce: false);
            return;
        }

        CCSPlayerController? winner = GetOpponent(victim!);
        string gameLabel = GetGameLabel(_activeGame);

        if (winner is not null && winner.IsValid && winner.PawnIsAlive)
        {
            BroadcastChat(
                $"[Jailbreak] LR {gameLabel} 종료: {winner.PlayerName} 승리");

            winner.PrintToCenterHtml(
                "<font color='#66BB6A' size='24'><b>LR 승리</b></font>",
                4);

            _logger.LogInformation(
                "[Jailbreak] Last request finished. Game: {Game}, Winner: {Winner}, WinnerSteamID: {WinnerSteamID}, Loser: {Loser}, LoserSteamID: {LoserSteamID}",
                _activeGame,
                winner.PlayerName,
                winner.SteamID,
                victim!.PlayerName,
                victim.SteamID);
        }
        else
        {
            BroadcastChat(
                $"[Jailbreak] LR {gameLabel}이 종료되었습니다.");

            _logger.LogInformation(
                "[Jailbreak] Last request finished without winner. Game: {Game}, Victim: {Victim}, VictimSteamID: {VictimSteamID}",
                _activeGame,
                victim!.PlayerName,
                victim.SteamID);
        }

        Clear();
    }

    public void ClearPlayer(
        CCSPlayerController? player,
        string reason,
        bool announce)
    {
        if (!IsValidHumanPlayer(player))
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !IsActiveParticipant(player))
            {
                return;
            }
        }

        if (player!.SteamID != 0 &&
            _playerStateManager.TryGet(
                player!.SteamID,
                out JailPlayerState? state) &&
            state is not null)
        {
            state.IsInLastRequest = false;
        }

        if (IsActiveParticipant(player))
        {
            Cancel(reason, announce);
        }
    }

    public void ClearPlayer(
        ulong steamId,
        string reason,
        bool announce)
    {
        if (steamId == 0)
        {
            return;
        }

        if (_playerStateManager.TryGet(
                steamId,
                out JailPlayerState? state) &&
            state is not null)
        {
            state.IsInLastRequest = false;
        }

        _lastNoScopeWarningAt.Remove(steamId);

        if (_currentLastRequestSteamId == steamId ||
            _opponentSteamId == steamId)
        {
            Cancel(reason, announce);
        }
    }

    public void HandleZoomPressed(CCSPlayerController? player)
    {
        if (_activeGame != LastRequestGame.NoScope ||
            !IsActiveParticipant(player))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_lastNoScopeWarningAt.TryGetValue(
                player!.SteamID,
                out DateTimeOffset lastWarningAt) &&
            (now - lastWarningAt).TotalSeconds < 2)
        {
            return;
        }

        _lastNoScopeWarningAt[player.SteamID] = now;

        player.PrintToChat(
            "[Jailbreak] 노스코프전에서는 조준경을 사용하지 마세요.");

        _logger.LogInformation(
            "[Jailbreak] No-scope zoom attempt detected. Player: {PlayerName}, SteamID: {SteamID}",
            player.PlayerName,
            player.SteamID);
    }

    private bool IsActiveParticipant(CCSPlayerController? player)
    {
        if (!TeamManager.IsGameplayParticipant(player))
        {
            return false;
        }

        return MatchesParticipant(
                player!,
                _currentLastRequestSteamId,
                _currentLastRequestSlot) ||
            MatchesParticipant(
                player!,
                _opponentSteamId,
                _opponentSlot);
    }

    private bool CanOpenMenu(
        CCSPlayerController? player,
        out string error)
    {
        error = string.Empty;

        if (!IsValidHumanPlayer(player) ||
            !TeamManager.IsPrisoner(player) ||
            !player!.PawnIsAlive)
        {
            error = "살아 있는 마지막 죄수만 사용할 수 있습니다.";
            return false;
        }

        EnsureLastRequestCandidate(player, "can open menu");

        if (!MatchesParticipant(player, _currentLastRequestSteamId, _currentLastRequestSlot))
        {
            error = "아직 LR 가능 상태가 아닙니다.";
            return false;
        }

        if (_roundManager.State.IsFreedayRound)
        {
            error = "전체 자유시간 중에는 LR을 시작할 수 없습니다.";
            return false;
        }

        if (_activeGame != LastRequestGame.None)
        {
            error = "이미 진행 중인 LR이 있습니다.";
            return false;
        }

        if (!GetAliveGuards().Any())
        {
            error = "상대할 살아 있는 간수가 없습니다.";
            return false;
        }

        return true;
    }

    private bool EnsureLastRequestCandidate(
        CCSPlayerController? player,
        string reason)
    {
        if (!IsValidHumanPlayer(player) ||
            !TeamManager.IsPrisoner(player) ||
            !player!.PawnIsAlive)
        {
            return false;
        }

        if (_roundManager.State.IsFreedayRound ||
            _closedForRound ||
            _activeGame != LastRequestGame.None)
        {
            return false;
        }

        if (MatchesParticipant(player, _currentLastRequestSteamId, _currentLastRequestSlot))
        {
            return true;
        }

        List<CCSPlayerController> aliveHumanPrisoners = Utilities.GetPlayers()
            .Where(IsAliveLastRequestBlockingPrisoner)
            .ToList();

        if (aliveHumanPrisoners.Count != 1 ||
            aliveHumanPrisoners[0].Slot != player.Slot ||
            !GetAliveGuards().Any())
        {
            _logger.LogInformation(
                "[Jailbreak] LR candidate fallback rejected. Reason: {Reason}, Player: {PlayerName}, SteamID: {SteamID}, Slot: {Slot}, RoundActive: {RoundActive}, AlivePrisoners: {AlivePrisoners}, AliveGuards: {AliveGuards}, BotsBlockLastRequest: {BotsBlockLastRequest}",
                reason,
                player.PlayerName,
                player.SteamID,
                player.Slot,
                _roundManager.State.IsActive,
                aliveHumanPrisoners.Count,
                GetAliveGuards().Count(),
                _botsBlockLastRequest);
            return false;
        }

        JailPlayerState? state =
            _playerStateManager.GetOrCreate(player);

        if (state is null)
        {
            return false;
        }

        ClearActiveState();

        state.IsInLastRequest = true;
        _currentLastRequestSteamId = player.SteamID;
        _currentLastRequestSlot = player.Slot;
        _closedForRound = false;

        _logger.LogWarning(
            "[Jailbreak] LR candidate recovered from command. Reason: {Reason}, Player: {PlayerName}, SteamID: {SteamID}, Slot: {Slot}, RoundActive: {RoundActive}, Round: {RoundNumber}",
            reason,
            player.PlayerName,
            player.SteamID,
            player.Slot,
            _roundManager.State.IsActive,
            _roundManager.State.RoundNumber);

        return true;
    }

    private void OpenOpponentMenu(
        CCSPlayerController prisoner,
        LastRequestGame game)
    {
        if (!CanOpenMenu(prisoner, out string error))
        {
            MenuManager.CloseActiveMenu(prisoner);
            prisoner.PrintToChat($"[Jailbreak] LR 메뉴를 열 수 없습니다. {error}");
            return;
        }

        List<CCSPlayerController> guards = GetAliveGuards()
            .OrderBy(guard => guard.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CenterHtmlMenu menu = new($"LR - {GetGameLabel(game)} 상대 선택", _plugin) { ExitButton = true };

        foreach (CCSPlayerController guard in guards)
        {
            int guardSlot = guard.Slot;
            string guardName = guard.PlayerName;

            menu.AddMenuOption(
                guardName,
                (selectedPlayer, _) =>
                {
                    MenuManager.CloseActiveMenu(selectedPlayer);

                    CCSPlayerController? selectedGuard =
                        Utilities.GetPlayerFromSlot(guardSlot);

                    if (!StartGame(
                            selectedPlayer,
                            selectedGuard,
                            game,
                            out string startError))
                    {
                        selectedPlayer.PrintToChat(
                            $"[Jailbreak] LR 시작 실패: {startError}");
                    }
                });
        }

        menu.Open(prisoner);
    }

    private bool StartGame(
        CCSPlayerController prisoner,
        CCSPlayerController? guard,
        LastRequestGame game,
        out string error)
    {
        error = string.Empty;

        if (!CanOpenMenu(prisoner, out error))
        {
            return false;
        }

        if (!TeamManager.IsGameplayParticipant(guard) ||
            !IsAliveGuard(guard))
        {
            error = "선택한 간수가 더 이상 살아 있지 않습니다.";
            return false;
        }

        try
        {
            ApplyLoadout(prisoner, game);
            ApplyLoadout(guard!, game);
        }
        catch (Exception exception)
        {
            error = "무기 지급 중 오류가 발생했습니다.";

            _logger.LogWarning(
                exception,
                "[Jailbreak] Last request loadout failed. Game: {Game}, Prisoner: {Prisoner}, Guard: {Guard}",
                game,
                prisoner.PlayerName,
                guard!.PlayerName);

            return false;
        }

        _activeGame = game;
        _opponentSteamId = guard!.SteamID;
        _opponentSlot = guard.Slot;
        _closedForRound = false;

        string gameLabel = GetGameLabel(game);

        BroadcastChat(
            $"[Jailbreak] LR {gameLabel}: {prisoner.PlayerName} vs {guard.PlayerName}");

        if (game == LastRequestGame.NoScope)
        {
            BroadcastChat("[Jailbreak] 노스코프전 규칙: 조준경 사용 없이 진행합니다.");
        }

        _logger.LogInformation(
            "[Jailbreak] Last request started. Game: {Game}, Prisoner: {Prisoner}, PrisonerSteamID: {PrisonerSteamID}, Guard: {Guard}, GuardSteamID: {GuardSteamID}",
            game,
            prisoner.PlayerName,
            prisoner.SteamID,
            guard.PlayerName,
            guard.SteamID);

        return true;
    }

    private static void ApplyLoadout(
        CCSPlayerController player,
        LastRequestGame game)
    {
        player.RemoveWeapons();
        player.GiveNamedItem("weapon_knife");

        switch (game)
        {
            case LastRequestGame.Pistol:
                player.GiveNamedItem("weapon_deagle");
                break;

            case LastRequestGame.NoScope:
                player.GiveNamedItem("weapon_ssg08");
                break;
        }
    }

    private void StartLoadoutTimer()
    {
        StopLoadoutTimer();

        if (_activeGame == LastRequestGame.None)
        {
            return;
        }

        _loadoutTimer = _plugin.AddTimer(
            1.0f,
            EnforceParticipantLoadouts,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StopLoadoutTimer()
    {
        _loadoutTimer?.Kill();
        _loadoutTimer = null;
    }

    private void EnforceParticipantLoadouts()
    {
        if (_activeGame == LastRequestGame.None)
        {
            StopLoadoutTimer();
            return;
        }

        // 1초마다 도는 타이머이므로 GetPlayers 풀스캔을 두 번 하지 않도록
        // 한 번의 순회로 죄수와 상대를 함께 찾습니다.
        CCSPlayerController? prisoner = null;
        CCSPlayerController? opponent = null;

        foreach (CCSPlayerController candidate in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(candidate))
            {
                continue;
            }

            if (prisoner is null &&
                MatchesParticipant(candidate, _currentLastRequestSteamId, _currentLastRequestSlot))
            {
                prisoner = candidate;
            }
            else if (opponent is null &&
                MatchesParticipant(candidate, _opponentSteamId, _opponentSlot))
            {
                opponent = candidate;
            }

            if (prisoner is not null && opponent is not null)
            {
                break;
            }
        }

        if (!TeamManager.IsGameplayParticipant(prisoner) ||
            !prisoner!.PawnIsAlive ||
            !TeamManager.IsGameplayParticipant(opponent) ||
            !opponent!.PawnIsAlive)
        {
            return;
        }

        try
        {
            EnforceAllowedWeapons(prisoner, _activeGame);
            EnforceAllowedWeapons(opponent, _activeGame);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "[Jailbreak] Last request loadout enforcement failed. Game: {Game}, PrisonerSteamID: {PrisonerSteamID}, OpponentSteamID: {OpponentSteamID}",
                _activeGame,
                _currentLastRequestSteamId,
                _opponentSteamId);
        }
    }

    private static void EnforceAllowedWeapons(
        CCSPlayerController player,
        LastRequestGame game)
    {
        List<string> weaponNames = GetPlayerWeaponNames(player);

        // Avoid removing live weapon entities during LR enforcement. On CS2 this can
        // destabilize the server when bots or freshly picked-up weapons are involved.
        // Disallowed weapons are blocked in the damage hook instead.
        EnsureRequiredWeapons(player, game, weaponNames);
    }

    private static List<string> GetPlayerWeaponNames(CCSPlayerController player)
    {
        List<string> weaponNames = new();
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        if (pawn?.WeaponServices is null)
        {
            return weaponNames;
        }

        foreach (CHandle<CBasePlayerWeapon> weaponHandle in pawn.WeaponServices.MyWeapons)
        {
            CBasePlayerWeapon? weapon = weaponHandle.Value;

            if (weapon is null || !weapon.IsValid)
            {
                continue;
            }

            string? weaponName = weapon.GetWeaponName();

            if (!string.IsNullOrWhiteSpace(weaponName))
            {
                weaponNames.Add(weaponName);
            }
        }

        return weaponNames;
    }

    private static string? GetActiveWeaponName(CCSPlayerController player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        CBasePlayerWeapon? activeWeapon =
            pawn?.WeaponServices?.ActiveWeapon.Value;

        return activeWeapon is not null && activeWeapon.IsValid
            ? activeWeapon.GetWeaponName()
            : null;
    }

    private static void EnsureRequiredWeapons(
        CCSPlayerController player,
        LastRequestGame game,
        IReadOnlyCollection<string> currentWeapons)
    {
        EnsureWeapon(player, currentWeapons, "weapon_knife");

        switch (game)
        {
            case LastRequestGame.Pistol:
                EnsureWeapon(player, currentWeapons, "weapon_deagle");
                break;

            case LastRequestGame.NoScope:
                EnsureWeapon(player, currentWeapons, "weapon_ssg08");
                break;
        }
    }

    private static void EnsureWeapon(
        CCSPlayerController player,
        IReadOnlyCollection<string> currentWeapons,
        string weaponName)
    {
        if (currentWeapons.Any(current => HasRequiredWeapon(current, weaponName)))
        {
            return;
        }

        player.GiveNamedItem(weaponName);
    }

    private static bool IsAllowedWeapon(
        LastRequestGame game,
        string? weaponName)
    {
        if (IsKnife(weaponName))
        {
            return true;
        }

        return game switch
        {
            LastRequestGame.Pistol => IsSameWeapon(weaponName, "weapon_deagle"),
            LastRequestGame.NoScope => IsSameWeapon(weaponName, "weapon_ssg08"),
            _ => false
        };
    }

    private static bool IsKnife(string? weaponName)
    {
        return weaponName is not null &&
            weaponName.StartsWith("weapon_knife", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRequiredWeapon(
        string? actualWeaponName,
        string expectedWeaponName)
    {
        if (IsSameWeapon(expectedWeaponName, "weapon_knife"))
        {
            return IsKnife(actualWeaponName);
        }

        return IsSameWeapon(actualWeaponName, expectedWeaponName);
    }

    private static bool IsSameWeapon(
        string? actualWeaponName,
        string expectedWeaponName)
    {
        return string.Equals(
            actualWeaponName,
            expectedWeaponName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<CCSPlayerController> GetAliveGuards()
    {
        return Utilities.GetPlayers()
            .Where(player =>
                IsAliveGuard(player));
    }

    private static bool IsAliveGuard(CCSPlayerController? player)
    {
        return TeamManager.IsGuard(player)
            && player!.PawnIsAlive;
    }

    private CCSPlayerController? GetOpponent(CCSPlayerController player)
    {
        return MatchesParticipant(
                player,
                _currentLastRequestSteamId,
                _currentLastRequestSlot)
            ? GetParticipant(_opponentSteamId, _opponentSlot)
            : GetParticipant(_currentLastRequestSteamId, _currentLastRequestSlot);
    }

    private static CCSPlayerController? GetParticipant(
        ulong steamId,
        int slot)
    {
        return Utilities.GetPlayers()
            .FirstOrDefault(candidate =>
                TeamManager.IsGameplayParticipant(candidate) &&
                MatchesParticipant(candidate, steamId, slot));
    }

    private static bool IsAliveHumanPrisoner(CCSPlayerController player)
    {
        return IsValidHumanPlayer(player)
            && TeamManager.IsPrisoner(player)
            && player.PawnIsAlive;
    }

    private bool IsAliveLastRequestBlockingPrisoner(CCSPlayerController player)
    {
        if (!TeamManager.IsPrisoner(player) ||
            !player.PawnIsAlive)
        {
            return false;
        }

        return _botsBlockLastRequest ||
            IsValidHumanPlayer(player);
    }

    private static bool MatchesParticipant(
        CCSPlayerController player,
        ulong steamId,
        int slot)
    {
        if (steamId != 0)
        {
            return player.SteamID == steamId;
        }

        return slot >= 0 && player.Slot == slot;
    }

    private static bool IsValidHumanPlayer(CCSPlayerController? player)
    {
        return TeamManager.IsHumanPlayer(player);
    }

    private static void BroadcastChat(string message)
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (IsValidHumanPlayer(player))
            {
                player.PrintToChat(message);
            }
        }
    }

    private static string GetGameLabel(LastRequestGame game)
    {
        return game switch
        {
            LastRequestGame.Knife => "칼전",
            LastRequestGame.Pistol => "권총전",
            LastRequestGame.NoScope => "노스코프전",
            _ => "알 수 없음"
        };
    }
}
