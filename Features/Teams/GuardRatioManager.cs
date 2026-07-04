using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace Jailbreak.Features.Teams;

public sealed record GuardRatioSnapshot(
    int PrisonerCount,
    int GuardCount,
    int ProjectedPrisonerCount,
    int ProjectedGuardCount,
    int MaximumGuardCount,
    int PrisonersPerGuard,
    bool PlayerAlreadyGuard,
    bool CanJoinGuard);

public sealed class GuardRatioManager
{
    public GuardRatioManager(int prisonersPerGuard = 3)
    {
        if (prisonersPerGuard < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(prisonersPerGuard),
                "Prisoners per guard must be at least 1.");
        }

        PrisonersPerGuard = prisonersPerGuard;
    }

    public int PrisonersPerGuard { get; }

    public bool CanJoinGuard(CCSPlayerController? player)
    {
        return GetSnapshot(player)?.CanJoinGuard == true;
    }

    public GuardRatioSnapshot? GetSnapshot(CCSPlayerController? player)
    {
        if (!TeamManager.CountsForGuardRatio(player))
        {
            return null;
        }

        int prisonerCount = 0;
        int guardCount = 0;

        foreach (CCSPlayerController connectedPlayer in Utilities.GetPlayers())
        {
            if (!TeamManager.CountsForGuardRatio(connectedPlayer))
            {
                continue;
            }

            if (TeamManager.IsPrisoner(connectedPlayer))
            {
                prisonerCount++;
            }
            else if (TeamManager.IsGuard(connectedPlayer))
            {
                guardCount++;
            }
        }

        bool playerAlreadyGuard = IsHumanGuard(player);

        // A prisoner moving to CT leaves the prisoner team at the same time.
        int projectedPrisoners = TeamManager.IsPrisoner(player)
            ? Math.Max(0, prisonerCount - 1)
            : prisonerCount;

        int projectedGuards = playerAlreadyGuard
            ? guardCount
            : guardCount + 1;

        int maximumGuards = GetMaximumGuardCount(projectedPrisoners);
        bool canJoinGuard = playerAlreadyGuard || projectedGuards <= maximumGuards;

        return new GuardRatioSnapshot(
            prisonerCount,
            guardCount,
            projectedPrisoners,
            projectedGuards,
            maximumGuards,
            PrisonersPerGuard,
            playerAlreadyGuard,
            canJoinGuard);
    }

    public int GetMaximumGuardCount(int prisonerCount)
    {
        if (prisonerCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(prisonerCount));
        }

        // Keep one guard slot available on an empty or low-population server.
        return Math.Max(
            1,
            (prisonerCount + PrisonersPerGuard - 1) / PrisonersPerGuard);
    }

    private static bool IsHumanGuard(CCSPlayerController? player)
    {
        return TeamManager.CountsForGuardRatio(player) &&
            TeamManager.IsGuard(player);
    }
}
