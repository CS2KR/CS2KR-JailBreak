using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace Jailbreak.Features.Teams;

public sealed class TeamSwapManager
{
    private readonly ILogger _logger;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _declineCooldown;
    private readonly Dictionary<ulong, TeamSwapRequest> _requestsByTarget = new();
    private readonly Dictionary<(ulong Requester, ulong Target), DateTimeOffset> _blockedUntil = new();

    public TeamSwapManager(
        ILogger logger,
        TimeSpan requestTimeout,
        TimeSpan declineCooldown)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _requestTimeout = requestTimeout;
        _declineCooldown = declineCooldown;
    }

    public bool RequestSwap(
        CCSPlayerController requester,
        CCSPlayerController target,
        out string error)
    {
        error = string.Empty;

        if (!CanSwap(requester, target, out error))
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        (ulong Requester, ulong Target) key = (requester.SteamID, target.SteamID);

        if (_blockedUntil.TryGetValue(key, out DateTimeOffset blockedUntil) &&
            blockedUntil > now)
        {
            int remainingSeconds = Math.Max(
                1,
                (int)Math.Ceiling((blockedUntil - now).TotalSeconds));
            error = $"최근 거절된 대상입니다. {remainingSeconds}초 후 다시 요청하세요.";
            return false;
        }

        _requestsByTarget[target.SteamID] = new TeamSwapRequest(
            requester.SteamID,
            target.SteamID,
            requester.PlayerName,
            now.Add(_requestTimeout));

        requester.PrintToChat(
            $"[Jailbreak] {target.PlayerName}에게 팀교환 요청을 보냈습니다.");
        target.PrintToChat(
            $"[Jailbreak] {requester.PlayerName}님이 팀교환을 요청했습니다. 수락: css_jb team accept / 거절: css_jb team deny");

        _logger.LogInformation(
            "[Jailbreak] Team swap requested. Requester: {Requester}, RequesterSteamID: {RequesterSteamId}, Target: {Target}, TargetSteamID: {TargetSteamId}",
            requester.PlayerName,
            requester.SteamID,
            target.PlayerName,
            target.SteamID);

        return true;
    }

    public bool Accept(
        CCSPlayerController target,
        out string error)
    {
        error = string.Empty;

        if (!TryGetActiveRequest(
                target,
                removeExpired: true,
                out TeamSwapRequest request,
                out error))
        {
            return false;
        }

        CCSPlayerController? requester =
            FindHumanPlayerBySteamId(request.RequesterSteamId);

        if (!CanSwap(requester, target, out error))
        {
            _requestsByTarget.Remove(target.SteamID);
            return false;
        }

        CsTeam requesterTeam = requester!.Team;
        CsTeam targetTeam = target.Team;

        requester.ChangeTeam(targetTeam);
        target.ChangeTeam(requesterTeam);

        _requestsByTarget.Remove(target.SteamID);

        BroadcastChat(
            $"[Jailbreak] {requester.PlayerName}님과 {target.PlayerName}님이 팀을 교환했습니다.");

        _logger.LogInformation(
            "[Jailbreak] Team swap accepted. Requester: {Requester}, RequesterSteamID: {RequesterSteamId}, Target: {Target}, TargetSteamID: {TargetSteamId}",
            requester.PlayerName,
            requester.SteamID,
            target.PlayerName,
            target.SteamID);

        return true;
    }

    public bool Decline(
        CCSPlayerController target,
        out string error)
    {
        error = string.Empty;

        if (!TryGetActiveRequest(
                target,
                removeExpired: true,
                out TeamSwapRequest request,
                out error))
        {
            return false;
        }

        _requestsByTarget.Remove(target.SteamID);
        _blockedUntil[(request.RequesterSteamId, target.SteamID)] =
            DateTimeOffset.UtcNow.Add(_declineCooldown);

        CCSPlayerController? requester =
            FindHumanPlayerBySteamId(request.RequesterSteamId);

        requester?.PrintToChat(
            $"[Jailbreak] {target.PlayerName}님이 팀교환 요청을 거절했습니다.");
        target.PrintToChat("[Jailbreak] 팀교환 요청을 거절했습니다.");

        _logger.LogInformation(
            "[Jailbreak] Team swap declined. RequesterSteamID: {RequesterSteamId}, Target: {Target}, TargetSteamID: {TargetSteamId}",
            request.RequesterSteamId,
            target.PlayerName,
            target.SteamID);

        return true;
    }

    public void Clear()
    {
        _requestsByTarget.Clear();
        _blockedUntil.Clear();
    }

    public void RemovePlayer(ulong steamId)
    {
        _requestsByTarget.Remove(steamId);

        foreach (ulong targetSteamId in _requestsByTarget
                     .Where(pair => pair.Value.RequesterSteamId == steamId)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _requestsByTarget.Remove(targetSteamId);
        }
    }

    public CCSPlayerController? FindTarget(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        string trimmedQuery = query.Trim();
        List<CCSPlayerController> players = Utilities.GetPlayers()
            .Where(TeamManager.IsHumanPlayer)
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

    private bool TryGetActiveRequest(
        CCSPlayerController target,
        bool removeExpired,
        out TeamSwapRequest request,
        out string error)
    {
        request = default;
        error = string.Empty;

        if (!_requestsByTarget.TryGetValue(target.SteamID, out request))
        {
            error = "받은 팀교환 요청이 없습니다.";
            return false;
        }

        if (request.ExpiresAtUtc >= DateTimeOffset.UtcNow)
        {
            return true;
        }

        if (removeExpired)
        {
            _requestsByTarget.Remove(target.SteamID);
        }

        error = "팀교환 요청이 만료되었습니다.";
        return false;
    }

    private static bool CanSwap(
        CCSPlayerController? requester,
        CCSPlayerController? target,
        out string error)
    {
        error = string.Empty;

        if (!TeamManager.IsHumanPlayer(requester) ||
            !TeamManager.IsHumanPlayer(target))
        {
            error = "대상 플레이어를 확인할 수 없습니다.";
            return false;
        }

        if (requester!.SteamID == target!.SteamID)
        {
            error = "자기 자신에게는 팀교환을 요청할 수 없습니다.";
            return false;
        }

        if (!TeamManager.IsPlayableTeam(requester) ||
            !TeamManager.IsPlayableTeam(target))
        {
            error = "죄수와 간수 사이에서만 팀교환할 수 있습니다.";
            return false;
        }

        if (requester.Team == target.Team)
        {
            error = "서로 다른 팀끼리만 팀교환할 수 있습니다.";
            return false;
        }

        return true;
    }

    private static CCSPlayerController? FindHumanPlayerBySteamId(ulong steamId)
    {
        return Utilities.GetPlayers()
            .FirstOrDefault(player =>
                TeamManager.IsHumanPlayer(player) &&
                player.SteamID == steamId);
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

    private readonly record struct TeamSwapRequest(
        ulong RequesterSteamId,
        ulong TargetSteamId,
        string RequesterName,
        DateTimeOffset ExpiresAtUtc);
}
