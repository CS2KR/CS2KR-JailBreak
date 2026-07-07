namespace Jailbreak.Features.Duel;

public sealed class OneVsOneDuelManager
{
    public bool IsActive { get; private set; }
    public ulong PrisonerSteamId { get; private set; }
    public ulong GuardSteamId { get; private set; }
    public string PrisonerName { get; private set; } = string.Empty;
    public string GuardName { get; private set; } = string.Empty;

    public string StatusText =>
        IsActive
            ? $"진행 중: {GetParticipantText()}"
            : "없음";

    public void Reset()
    {
        IsActive = false;
        PrisonerSteamId = 0;
        GuardSteamId = 0;
        PrisonerName = string.Empty;
        GuardName = string.Empty;
    }

    public bool Evaluate(
        int alivePrisonerCount,
        int aliveGuardCount,
        ulong prisonerSteamId,
        ulong guardSteamId,
        string? prisonerName,
        string? guardName,
        out bool ended)
    {
        ended = false;
        alivePrisonerCount = Math.Max(0, alivePrisonerCount);
        aliveGuardCount = Math.Max(0, aliveGuardCount);

        bool shouldBeActive =
            alivePrisonerCount == 1 &&
            aliveGuardCount == 1;

        if (shouldBeActive)
        {
            string normalizedPrisonerName =
                string.IsNullOrWhiteSpace(prisonerName)
                    ? "마지막 죄수"
                    : prisonerName.Trim();
            string normalizedGuardName =
                string.IsNullOrWhiteSpace(guardName)
                    ? "마지막 간수"
                    : guardName.Trim();

            bool changed =
                !IsActive ||
                PrisonerSteamId != prisonerSteamId ||
                GuardSteamId != guardSteamId ||
                !string.Equals(
                    PrisonerName,
                    normalizedPrisonerName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    GuardName,
                    normalizedGuardName,
                    StringComparison.Ordinal);

            IsActive = true;
            PrisonerSteamId = prisonerSteamId;
            GuardSteamId = guardSteamId;
            PrisonerName = normalizedPrisonerName;
            GuardName = normalizedGuardName;

            return changed;
        }

        if (IsActive)
        {
            Reset();
            ended = true;
        }

        return false;
    }

    public string GetParticipantText()
    {
        if (!IsActive)
        {
            return "없음";
        }

        return $"{PrisonerName} vs {GuardName}";
    }
}
