namespace Jailbreak.Features.Incidents;

public sealed record JailIncident(
    DateTimeOffset CreatedAt,
    string Text);

public sealed class IncidentLogManager
{
    private readonly int _capacity;
    private readonly Queue<JailIncident> _incidents = new();

    public IncidentLogManager(int capacity = 10)
    {
        _capacity = Math.Max(1, capacity);
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _incidents.Enqueue(new JailIncident(
            DateTimeOffset.UtcNow,
            text.Trim()));

        while (_incidents.Count > _capacity)
        {
            _incidents.Dequeue();
        }
    }

    public IReadOnlyList<JailIncident> GetRecent()
    {
        return _incidents
            .Reverse()
            .ToList();
    }

    public string GetLatestText()
    {
        return _incidents.Count == 0
            ? string.Empty
            : _incidents.Last().Text;
    }

    public void Clear()
    {
        _incidents.Clear();
    }
}
