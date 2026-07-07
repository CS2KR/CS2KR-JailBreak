namespace Jailbreak.Features.Freekill;

public sealed class FreekillTimeManager
{
    private int? _lastAliveGuardCount;
    private int _roundStartAliveGuardCount;

    public bool IsActive { get; private set; }

    public string StatusText =>
        IsActive
            ? "진행 중"
            : "없음";

    public void Reset()
    {
        _lastAliveGuardCount = null;
        _roundStartAliveGuardCount = 0;
        IsActive = false;
    }

    public void CaptureBaseline(int aliveGuardCount)
    {
        int normalizedCount = Math.Max(0, aliveGuardCount);
        _lastAliveGuardCount = normalizedCount;
        _roundStartAliveGuardCount = normalizedCount;
        IsActive = false;
    }

    public bool Evaluate(
        int aliveGuardCount,
        int alivePrisonerCount,
        out bool ended)
    {
        ended = false;
        aliveGuardCount = Math.Max(0, aliveGuardCount);
        alivePrisonerCount = Math.Max(0, alivePrisonerCount);

        int previousAliveGuardCount =
            _lastAliveGuardCount ?? aliveGuardCount;

        bool shouldStart =
            !IsActive &&
            _roundStartAliveGuardCount >= 3 &&
            previousAliveGuardCount > 1 &&
            aliveGuardCount == 1 &&
            alivePrisonerCount > 0;

        if (shouldStart)
        {
            IsActive = true;
        }
        else if (IsActive && aliveGuardCount != 1)
        {
            IsActive = false;
            ended = true;
        }

        _lastAliveGuardCount = aliveGuardCount;
        return shouldStart;
    }
}
