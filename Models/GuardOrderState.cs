namespace Jailbreak.Models;

public sealed class GuardOrderState
{
    public bool IsActive { get; private set; }
    public bool IsCustomText { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;
    public int DeadlineRoundSeconds { get; private set; }
    public ulong IssuerSteamId { get; private set; }
    public string IssuerName { get; private set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; private set; }

    public void SetTimed(
        string displayText,
        int deadlineRoundSeconds,
        ulong issuerSteamId,
        string issuerName)
    {
        IsActive = true;
        IsCustomText = false;
        DisplayText = displayText;
        DeadlineRoundSeconds = deadlineRoundSeconds;
        IssuerSteamId = issuerSteamId;
        IssuerName = issuerName;
        IssuedAtUtc = DateTimeOffset.UtcNow;
    }

    public void SetCustom(
        string displayText,
        ulong issuerSteamId,
        string issuerName)
    {
        IsActive = true;
        IsCustomText = true;
        DisplayText = displayText;
        DeadlineRoundSeconds = 0;
        IssuerSteamId = issuerSteamId;
        IssuerName = issuerName;
        IssuedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Clear()
    {
        IsActive = false;
        IsCustomText = false;
        DisplayText = string.Empty;
        DeadlineRoundSeconds = 0;
        IssuerSteamId = 0;
        IssuerName = string.Empty;
        IssuedAtUtc = default;
    }
}
