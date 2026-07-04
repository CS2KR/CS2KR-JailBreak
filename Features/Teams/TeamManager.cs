using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace Jailbreak.Features.Teams;

public sealed class TeamManager
{
    public static bool IsPrisoner(CCSPlayerController? player)
    {
        return IsGameplayParticipant(player)
            && player!.Team == CsTeam.Terrorist;
    }

    public static bool IsGuard(CCSPlayerController? player)
    {
        return IsGameplayParticipant(player)
            && player!.Team == CsTeam.CounterTerrorist;
    }

    public static bool IsPlayableTeam(CCSPlayerController? player)
    {
        return IsPrisoner(player) || IsGuard(player);
    }

    public static bool IsGameplayParticipant(CCSPlayerController? player)
    {
        return player is not null
            && player.IsValid
            && !player.IsHLTV
            && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    }

    public static bool IsHumanPlayer(CCSPlayerController? player)
    {
        return player is not null
            && player.IsValid
            && !player.IsBot
            && !player.IsHLTV
            && player.SteamID != 0;
    }

    public static bool CanUseCommand(CCSPlayerController? player)
    {
        return IsHumanPlayer(player);
    }

    public static bool CountsForGuardRatio(CCSPlayerController? player)
    {
        return IsHumanPlayer(player);
    }
}
