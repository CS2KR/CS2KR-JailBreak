using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using Jailbreak.Core;
using Jailbreak.Features.Teams;
using Jailbreak.Models;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.Freeday;

public sealed class FreedayManager
{
    private readonly PlayerStateManager _playerStateManager;
    private readonly RoundManager _roundManager;
    private readonly ILogger _logger;

    public FreedayManager(
        PlayerStateManager playerStateManager,
        RoundManager roundManager,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(playerStateManager);
        ArgumentNullException.ThrowIfNull(roundManager);
        ArgumentNullException.ThrowIfNull(logger);

        _playerStateManager = playerStateManager;
        _roundManager = roundManager;
        _logger = logger;
    }

    public bool StartGlobalFreeday()
    {
        JailRoundState roundState = _roundManager.State;

        if (!roundState.IsActive || roundState.IsFreedayRound)
        {
            return false;
        }

        roundState.StartFreeday();

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (IsValidHumanPlayer(player))
            {
                player.PrintToChat(
                    "[Jailbreak] 전체 자유시간이 시작되었습니다.");
            }
        }

        _logger.LogInformation(
            "[Jailbreak] Global freeday started. Round: {RoundNumber}, Map: {Map}",
            roundState.RoundNumber,
            roundState.CurrentMap);

        return true;
    }

    public bool EndGlobalFreeday(string reason)
    {
        JailRoundState roundState = _roundManager.State;

        if (!roundState.IsFreedayRound)
        {
            return false;
        }

        roundState.EndFreeday();

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (IsValidHumanPlayer(player))
            {
                player.PrintToChat(
                    $"[Jailbreak] 전체 자유시간이 종료되었습니다. 사유: {reason}");
            }
        }

        _logger.LogInformation(
            "[Jailbreak] Global freeday ended. Round: {RoundNumber}, Map: {Map}, Reason: {Reason}",
            roundState.RoundNumber,
            roundState.CurrentMap,
            reason);

        return true;
    }

    public void DisplayGlobalFreedayHud()
    {
        if (!_roundManager.State.IsFreedayRound)
        {
            return;
        }

        const string message =
            "<font color='#FFD700' size='28'><b>자유시간</b></font>";

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (IsValidHumanPlayer(player) &&
                MenuManager.GetActiveMenu(player) is null)
            {
                player.PrintToCenterHtml(message, 2);
            }
        }
    }

    public bool GrantPersonalFreeday(CCSPlayerController? target)
    {
        if (!_roundManager.State.IsActive ||
            _roundManager.State.IsFreedayRound ||
            !TeamManager.IsPrisoner(target))
        {
            return false;
        }

        JailPlayerState? state = _playerStateManager.GetOrCreate(target);

        if (state is null || state.IsRebel || state.IsFreeday)
        {
            return false;
        }

        state.IsFreeday = true;

        BroadcastChat(
            $"[Jailbreak] {target!.PlayerName}에게 개인 프리데이가 적용되었습니다.");

        _logger.LogInformation(
            "[Jailbreak] Personal freeday granted. Player: {PlayerName}, SteamID: {SteamId}",
            target.PlayerName,
            target.SteamID);

        return true;
    }

    public bool RemovePersonalFreeday(
        CCSPlayerController? target,
        string reason)
    {
        if (!TeamManager.IsGameplayParticipant(target))
        {
            return false;
        }

        CCSPlayerController trackedTarget = target!;
        JailPlayerState? state =
            _playerStateManager.GetOrCreate(trackedTarget);

        if (state is null || !state.IsFreeday)
        {
            return false;
        }

        state.IsFreeday = false;

        BroadcastChat(
            $"[Jailbreak] {trackedTarget.PlayerName}의 개인 프리데이가 해제되었습니다. 사유: {reason}");

        _logger.LogInformation(
            "[Jailbreak] Personal freeday removed. Player: {PlayerName}, SteamID: {SteamId}, Reason: {Reason}",
            trackedTarget.PlayerName,
            trackedTarget.SteamID,
            reason);

        return true;
    }

    public int ClearPersonalFreedays(
        string reason,
        bool announce)
    {
        int cleared = 0;

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !_playerStateManager.TryGet(
                    player,
                    out JailPlayerState? state) ||
                state?.IsFreeday != true)
            {
                continue;
            }

            state.IsFreeday = false;
            cleared++;
        }

        if (cleared > 0 && announce)
        {
            BroadcastChat(
                $"[Jailbreak] 개인 프리데이 {cleared}건이 정리되었습니다. 사유: {reason}");
        }

        if (cleared > 0)
        {
            _logger.LogInformation(
                "[Jailbreak] Personal freedays cleared. Count: {Count}, Reason: {Reason}",
                cleared,
                reason);
        }

        return cleared;
    }

    public IReadOnlyList<string> GetPersonalFreedayNames()
    {
        List<string> names = new();

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !_playerStateManager.TryGet(
                    player,
                    out JailPlayerState? state) ||
                state?.IsFreeday != true)
            {
                continue;
            }

            names.Add(player.PlayerName);
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void HandlePlayerDamage(
        CCSPlayerController? attacker,
        CCSPlayerController? victim,
        int damage)
    {
        if (damage <= 0 ||
            attacker is null ||
            victim is null ||
            attacker == victim)
        {
            return;
        }

        if (!TeamManager.IsPrisoner(attacker))
        {
            return;
        }

        RemovePersonalFreeday(attacker, "공격");
    }

    public CCSPlayerController? FindTarget(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        string trimmedQuery = query.Trim();
        List<CCSPlayerController> players = Utilities.GetPlayers()
            .Where(IsValidHumanPlayer)
            .ToList();

        if (ulong.TryParse(trimmedQuery, out ulong steamId))
        {
            return players.FirstOrDefault(player => player.SteamID == steamId);
        }

        CCSPlayerController? exactMatch = players.FirstOrDefault(
            player => string.Equals(
                player.PlayerName,
                trimmedQuery,
                StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        List<CCSPlayerController> partialMatches = players
            .Where(player => player.PlayerName.Contains(
                trimmedQuery,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return partialMatches.Count == 1
            ? partialMatches[0]
            : null;
    }

    private static bool IsValidHumanPlayer(CCSPlayerController player)
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
}
