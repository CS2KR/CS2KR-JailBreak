namespace Jailbreak.Core;

public sealed class OutputThrottleManager
{
    private readonly Dictionary<string, DateTimeOffset> _lastUsedAt =
        new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(
        string key,
        TimeSpan cooldown)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            cooldown <= TimeSpan.Zero)
        {
            return true;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (_lastUsedAt.TryGetValue(
                key,
                out DateTimeOffset lastUsedAt) &&
            now - lastUsedAt < cooldown)
        {
            return false;
        }

        _lastUsedAt[key] = now;
        return true;
    }

    public void Clear()
    {
        _lastUsedAt.Clear();
    }
}
