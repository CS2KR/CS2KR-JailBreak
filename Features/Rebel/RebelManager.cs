using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Jailbreak.Core;
using Jailbreak.Features.Teams;
using Jailbreak.Models;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.Rebel;

public sealed class RebelManager
{
    private static readonly Color RebelRenderColor = Color.FromArgb(255, 255, 0, 0);

    private readonly PlayerStateManager _playerStateManager;
    private readonly ILogger _logger;

    public RebelManager(
        PlayerStateManager playerStateManager,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(playerStateManager);
        ArgumentNullException.ThrowIfNull(logger);

        _playerStateManager = playerStateManager;
        _logger = logger;
    }

    public bool HandleGuardDamage(
        CCSPlayerController? attacker,
        CCSPlayerController? victim,
        int damage)
    {
        if (damage <= 0 ||
            attacker is null ||
            victim is null ||
            attacker == victim)
        {
            return false;
        }

        // 공격자는 실제 접속한 죄수만 허용
        if (!TeamManager.IsPrisoner(attacker))
        {
            return false;
        }

        // 피해자는 봇이어도 CT라면 간수로 인정
        if (!IsGuardVictim(victim))
        {
            return false;
        }

        JailPlayerState? state =
            _playerStateManager.GetOrCreate(attacker);

        if (state is null || state.IsRebel)
        {
            return false;
        }

        state.IsRebel = true;
        ApplyRebelColor(attacker, state);

        attacker.PrintToChat(
            " CS2.\x0BKR\x01｜ 간수를 공격하여 반란자가 되었습니다.");

        _logger.LogInformation(
            "[CS2.KR] Player became rebel. Player: {PlayerName}, SteamID: {SteamId}, Victim: {VictimName}, Damage: {Damage}",
            attacker.PlayerName,
            attacker.SteamID,
            victim.PlayerName,
            damage);

        return true;
    }

    public bool MarkRebel(
        CCSPlayerController? target,
        string reason)
    {
        if (!TeamManager.IsPrisoner(target) ||
            target!.PawnIsAlive != true)
        {
            return false;
        }

        JailPlayerState? state =
            _playerStateManager.GetOrCreate(target);

        if (state is null || state.IsRebel)
        {
            return false;
        }

        state.IsRebel = true;
        ApplyRebelColor(target!, state);

        BroadcastChat(
            $" CS2.\x0BKR\x01｜ {target!.PlayerName}이 반란자로 지정되었습니다. 사유: {reason}");

        _logger.LogInformation(
            "[CS2.KR] Player marked rebel. Player: {PlayerName}, SteamID: {SteamId}, Reason: {Reason}",
            target.PlayerName,
            target.SteamID,
            reason);

        return true;
    }

    public bool UnmarkRebel(
        CCSPlayerController? target,
        string reason)
    {
        if (!TeamManager.IsGameplayParticipant(target))
        {
            return false;
        }

        JailPlayerState? state =
            _playerStateManager.GetOrCreate(target);

        if (state is null || !state.IsRebel)
        {
            return false;
        }

        RestoreRebelColor(target!, state);
        state.IsRebel = false;

        BroadcastChat(
            $" CS2.\x0BKR\x01｜ {target!.PlayerName}의 반란자 상태가 해제되었습니다. 사유: {reason}");

        _logger.LogInformation(
            "[CS2.KR] Player unmarked rebel. Player: {PlayerName}, SteamID: {SteamId}, Reason: {Reason}",
            target.PlayerName,
            target.SteamID,
            reason);

        return true;
    }

    public IReadOnlyList<string> GetRebelNames()
    {
        List<string> names = new();

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !_playerStateManager.TryGet(
                    player,
                    out JailPlayerState? state) ||
                state?.IsRebel != true)
            {
                continue;
            }

            names.Add(player.PlayerName);
        }

        return names
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    public void RestoreRebel(CCSPlayerController? player)
    {
        if (player is null ||
            !player.IsValid ||
            player.IsHLTV ||
            !_playerStateManager.TryGet(
                player,
                out JailPlayerState? state) ||
            state is null)
        {
            return;
        }

        RestoreRebelColor(player!, state);
        state.IsRebel = false;
    }

    public void RestoreAllRebels()
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            RestoreRebel(player);
        }
    }

    public void RefreshRebelColors()
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!TeamManager.IsGameplayParticipant(player) ||
                !player.PawnIsAlive ||
                !_playerStateManager.TryGet(
                    player,
                    out JailPlayerState? state) ||
                state?.IsRebel != true)
            {
                continue;
            }

            ApplyRebelColor(player, state);
        }
    }

    private void ApplyRebelColor(
        CCSPlayerController player,
        JailPlayerState state)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        if (pawn is null || !pawn.IsValid)
        {
            _logger.LogDebug(
                "[CS2.KR] Rebel color skipped because player pawn is invalid. Player: {PlayerName}, SteamID: {SteamId}",
                player.PlayerName,
                player.SteamID);
            return;
        }

        state.SaveOriginalRenderColor(pawn.Render);

        // 매초 도는 타이머에서 이미 반란자 색이면 재기록하지 않아
        // 불필요한 네트워크 상태 변경을 피합니다. 리스폰으로 Render가
        // 초기화되면 색이 달라져 자동으로 다시 적용됩니다.
        if (pawn.Render == RebelRenderColor)
        {
            return;
        }

        pawn.Render = RebelRenderColor;

        _logger.LogDebug(
            "[CS2.KR] Rebel color applied. Player: {PlayerName}, SteamID: {SteamId}",
            player.PlayerName,
            player.SteamID);
    }

    private void RestoreRebelColor(
        CCSPlayerController player,
        JailPlayerState state)
    {
        if (!state.HasOriginalRenderColor)
        {
            return;
        }

        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        if (pawn is not null && pawn.IsValid)
        {
            pawn.Render = state.OriginalRenderColor;

            _logger.LogDebug(
                "[CS2.KR] Rebel color restored. Player: {PlayerName}, SteamID: {SteamId}",
                player.PlayerName,
                player.SteamID);
        }

        state.ClearOriginalRenderColor();
    }

    private static bool IsGuardVictim(CCSPlayerController victim)
    {
        return victim.IsValid
            && !victim.IsHLTV
            && victim.Team == CsTeam.CounterTerrorist;
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
}
