namespace Jailbreak.Models;

public sealed class GuardOrderState
{
    public bool IsActive { get; private set; }
    public bool IsCustomText { get; private set; }
    public string DisplayText { get; private set; } = string.Empty;
    public string PrimaryText { get; private set; } = string.Empty;
    public string ExtraText { get; private set; } = string.Empty;
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
        PrimaryText = displayText;
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
        PrimaryText = displayText;
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
        PrimaryText = string.Empty;
        ExtraText = string.Empty;
        DeadlineRoundSeconds = 0;
        IssuerSteamId = 0;
        IssuerName = string.Empty;
        IssuedAtUtc = default;
    }

    public void ClearPrimary()
    {
        IsActive = false;
        IsCustomText = false;
        DisplayText = string.Empty;
        PrimaryText = string.Empty;
        DeadlineRoundSeconds = 0;
        IssuerSteamId = 0;
        IssuerName = string.Empty;
        IssuedAtUtc = default;
    }

    public void SetExtra(string displayText)
    {
        ExtraText = displayText.Trim();
    }

    public void ClearExtra()
    {
        ExtraText = string.Empty;
    }
}
