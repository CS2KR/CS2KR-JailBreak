namespace Jailbreak.Models;

public sealed class JailRoundState
{
    public bool IsActive { get; private set; }
    public bool IsFreedayRound { get; private set; }
    public int RoundNumber { get; private set; }
    public string CurrentMap { get; private set; } = string.Empty;
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public bool LastChangePreviousIsActive { get; private set; }
    public bool LastChangeNewIsActive { get; private set; }
    public string LastChangeReason { get; private set; } = "initial";
    public DateTimeOffset LastChangeAtUtc { get; private set; } =
        DateTimeOffset.UtcNow;
    public string LastChangeEventName { get; private set; } = "initial";
    public string LastChangeMapName { get; private set; } = string.Empty;
    public int LastChangeRoundNumber { get; private set; }

    public void SetMap(string mapName)
    {
        CurrentMap = mapName;
    }

    public void StartRound(
        bool incrementRoundNumber = true,
        double elapsedSeconds = 0.0)
    {
        if (incrementRoundNumber)
        {
            RoundNumber++;
        }

        IsActive = true;
        IsFreedayRound = false;
        StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(
            -Math.Max(0.0, elapsedSeconds));
    }

    public void StartFreeday()
    {
        IsFreedayRound = true;
    }

    public void EndFreeday()
    {
        IsFreedayRound = false;
    }

    public void EndRound()
    {
        IsActive = false;
        IsFreedayRound = false;
        StartedAtUtc = null;
    }

    public void Reset(bool resetRoundNumber = false)
    {
        IsActive = false;
        IsFreedayRound = false;
        StartedAtUtc = null;

        if (resetRoundNumber)
        {
            RoundNumber = 0;
        }
    }

    public void RecordStateChange(
        bool previousIsActive,
        string reason,
        string eventName)
    {
        LastChangePreviousIsActive = previousIsActive;
        LastChangeNewIsActive = IsActive;
        LastChangeReason = reason;
        LastChangeAtUtc = DateTimeOffset.UtcNow;
        LastChangeEventName = eventName;
        LastChangeMapName = CurrentMap;
        LastChangeRoundNumber = RoundNumber;
    }
}
