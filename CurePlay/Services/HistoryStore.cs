namespace CurePlay.Services;

// Thread-safe, bounded store: 450 samples at 2 seconds ≈ 15 minutes.
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<(DateTime Time, double Value)>> _data = new();
    public void Add(string key, double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return;
        lock (_gate)
        {
            if (!_data.TryGetValue(key, out var q)) _data[key] = q = new();
            q.Enqueue((DateTime.Now, value.Value));
            while (q.Count > 450) q.Dequeue();
        }
    }
    public (DateTime Time, double Value)[] Get(string key, TimeSpan period)
    {
        lock (_gate)
        {
            if (!_data.TryGetValue(key, out var q)) return [];
            var since = DateTime.Now - period;
            return q.Where(x => x.Time >= since).ToArray();
        }
    }
}
