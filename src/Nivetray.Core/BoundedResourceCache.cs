namespace Nivetray.Core;

// Slow resource IO must never own the cache lock or accumulate blocked workers.
public sealed class BoundedResourceCache<T>(int capacity, Func<string, T?> load) where T : class
{
    private readonly object sync = new();
    private readonly Dictionary<string, T?> values = new(StringComparer.OrdinalIgnoreCase);
    private Task<T?>? pending;
    private string? pendingKey;
    public T? Cached(string key) { lock (sync) return values.GetValueOrDefault(key); }

    public async Task<T?> GetAsync(string key, TimeSpan timeout, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        Task<T?> work;
        lock (sync)
        {
            if (values.TryGetValue(key, out var value)) return value;
            if (pending is { IsCompleted: false })
            {
                if (!string.Equals(key, pendingKey, StringComparison.OrdinalIgnoreCase)) return null;
                work = pending;
            }
            else
            {
                pendingKey = key;
                work = pending = Task.Run(() =>
                {
                    var result = load(key); // No cache lock is held during IO.
                    lock (sync)
                    {
                        if (values.Count >= Math.Max(1, capacity)) values.Clear();
                        values[key] = result;
                    }
                    return result;
                });
            }
        }
        try { return await work.WaitAsync(timeout, cancellation); }
        catch (TimeoutException) { return null; }
    }
}
