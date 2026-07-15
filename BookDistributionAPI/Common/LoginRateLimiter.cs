using System.Collections.Concurrent;

namespace BookDistributionAPI.Common;

public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _attempts = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;

    public LoginRateLimiter(int maxAttempts = 5, int windowMinutes = 15)
    {
        _maxAttempts = maxAttempts;
        _window = TimeSpan.FromMinutes(windowMinutes);
    }

    public bool IsBlocked(string key)
    {
        if (_attempts.TryGetValue(key, out var window))
        {
            window.Cleanup();
            return window.Count >= _maxAttempts;
        }
        return false;
    }

    public void RecordAttempt(string key)
    {
        var window = _attempts.GetOrAdd(key, _ => new SlidingWindow(_window));
        window.Increment();
    }

    public void Reset(string key)
    {
        _attempts.TryRemove(key, out _);
    }

    private sealed class SlidingWindow
    {
        private readonly TimeSpan _window;
        private readonly ConcurrentQueue<DateTime> _timestamps = new();

        public SlidingWindow(TimeSpan window) => _window = window;

        public int Count
        {
            get
            {
                Cleanup();
                return _timestamps.Count;
            }
        }

        public void Increment()
        {
            Cleanup();
            _timestamps.Enqueue(DateTime.UtcNow);
        }

        public void Cleanup()
        {
            var cutoff = DateTime.UtcNow.Subtract(_window);
            while (_timestamps.TryPeek(out var ts) && ts < cutoff)
                _timestamps.TryDequeue(out _);
        }
    }
}
