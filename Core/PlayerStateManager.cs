using CounterStrikeSharp.API.Core;
using Jailbreak.Models;

namespace Jailbreak.Core;

public sealed class PlayerStateManager
{
    private const ulong NonSteamStateKeyPrefix = 1UL << 63;

    private readonly Dictionary<ulong, JailPlayerState> _states = new();

    public int Count => _states.Count;

    public JailPlayerState? GetOrCreate(CCSPlayerController? player)
    {
        if (!TryGetStateKey(player, out ulong stateKey))
        {
            return null;
        }

        if (_states.TryGetValue(stateKey, out JailPlayerState? state))
        {
            return state;
        }

        state = new JailPlayerState(stateKey);
        _states.Add(stateKey, state);
        return state;
    }

    public bool TryGet(ulong steamId, out JailPlayerState? state)
    {
        if (steamId == 0)
        {
            state = null;
            return false;
        }

        return _states.TryGetValue(steamId, out state);
    }

    public bool TryGet(
        CCSPlayerController? player,
        out JailPlayerState? state)
    {
        if (!TryGetStateKey(player, out ulong stateKey))
        {
            state = null;
            return false;
        }

        return _states.TryGetValue(stateKey, out state);
    }

    public bool Remove(ulong steamId)
    {
        return steamId != 0 && _states.Remove(steamId);
    }

    public bool Remove(CCSPlayerController? player)
    {
        return TryGetStateKey(player, out ulong stateKey) &&
            _states.Remove(stateKey);
    }

    public bool RemoveStateKey(ulong stateKey)
    {
        return stateKey != 0 && _states.Remove(stateKey);
    }

    public static bool TryGetSteamIdFromStateKey(
        ulong stateKey,
        out ulong steamId)
    {
        steamId = 0;

        if (stateKey == 0 ||
            (stateKey & NonSteamStateKeyPrefix) != 0)
        {
            return false;
        }

        steamId = stateKey;
        return true;
    }

    public void ResetRoundStates()
    {
        foreach (JailPlayerState state in _states.Values)
        {
            state.ResetRoundState();
        }
    }

    public void Clear()
    {
        _states.Clear();
    }

    public static bool TryGetStateKey(
        CCSPlayerController? player,
        out ulong stateKey)
    {
        stateKey = 0;

        if (player is null ||
            !player.IsValid ||
            player.IsHLTV)
        {
            return false;
        }

        if (player.SteamID != 0)
        {
            stateKey = player.SteamID;
            return true;
        }

        if (player.Slot < 0)
        {
            return false;
        }

        stateKey = NonSteamStateKeyPrefix | (uint)player.Slot;
        return true;
    }
}
