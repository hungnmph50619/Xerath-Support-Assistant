namespace XerathAssistant.Core;

// Independent reminder scheduling: no League process data or game state is used.
public sealed record ReminderItem(string Key, string Message, TimeSpan Interval);

public sealed class ReminderEngine
{
    private readonly Dictionary<string, TimeSpan> _nextDue = new(StringComparer.Ordinal);
    private ReminderItem[] _items = Array.Empty<ReminderItem>();
    private TimeSpan _lastAlert = TimeSpan.MinValue;
    private readonly TimeSpan _minimumGap;

    public ReminderEngine(TimeSpan minimumGap)
    {
        if (minimumGap < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumGap));
        _minimumGap = minimumGap;
    }

    public void Start(IEnumerable<ReminderItem> items)
    {
        _items = items.ToArray();
        if (_items.Any(item => string.IsNullOrWhiteSpace(item.Key) ||
                               string.IsNullOrWhiteSpace(item.Message) ||
                               item.Interval <= TimeSpan.Zero))
            throw new ArgumentException("Reminder keys, messages and intervals must be valid.", nameof(items));
        if (_items.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != _items.Length)
            throw new ArgumentException("Reminder keys must be unique.", nameof(items));

        _nextDue.Clear();
        foreach (var item in _items) _nextDue[item.Key] = item.Interval;
        _lastAlert = TimeSpan.MinValue;
    }

    // now is a monotonic elapsed session time; pause the Stopwatch to pause reminders.
    public string? Tick(TimeSpan now)
    {
        if (_items.Length == 0 || now < TimeSpan.Zero) return null;
        if (_lastAlert != TimeSpan.MinValue && now - _lastAlert < _minimumGap)
            return null;

        var due = _items.Where(item => now >= _nextDue[item.Key]).ToArray();
        if (due.Length == 0) return null;

        foreach (var item in due)
        {
            var next = _nextDue[item.Key];
            // Schedule one reminder per interval; do not replay missed notifications.
            var steps = (long)((now - next).Ticks / item.Interval.Ticks) + 1;
            _nextDue[item.Key] = next + TimeSpan.FromTicks(item.Interval.Ticks * steps);
        }
        _lastAlert = now;
        return string.Join(" ", due.Select(item => item.Message));
    }
}
