namespace XerathAssistant.Desktop;

public sealed record ChampionObservationTrack(
    string Champion,
    string Team,
    double LastX,
    double LastY,
    double LastConfidence,
    DateTimeOffset LastSeenUtc,
    int ObservationCount)
{
    public double LastSeenAgeSeconds(DateTimeOffset now) =>
        Math.Max(0, (now - LastSeenUtc).TotalSeconds);
}

/// <summary>
/// Tracks only positive, visible model observations. If a champion is not detected
/// in a later frame, the tracker does NOT infer where that champion moved.
/// </summary>
public sealed class MinimapChampionTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ChampionObservationTrack> _tracks =
        new(StringComparer.OrdinalIgnoreCase);

    public void Observe(
        DateTimeOffset observedAtUtc,
        IReadOnlyList<ChampionIconDetection> detections)
    {
        if (detections.Count == 0)
            return;

        lock (_sync)
        {
            foreach (var detection in detections
                         .GroupBy(
                             item => Key(item.Team, item.Champion),
                             StringComparer.OrdinalIgnoreCase)
                         .Select(group =>
                             group.OrderByDescending(item => item.Confidence).First()))
            {
                var key = Key(detection.Team, detection.Champion);
                var count = _tracks.TryGetValue(key, out var previous)
                    ? previous.ObservationCount + 1
                    : 1;

                _tracks[key] = new(
                    detection.Champion,
                    detection.Team,
                    detection.X,
                    detection.Y,
                    detection.Confidence,
                    observedAtUtc,
                    count);
            }

            var cutoff = observedAtUtc - TimeSpan.FromMinutes(3);
            foreach (var stale in _tracks
                         .Where(pair => pair.Value.LastSeenUtc < cutoff)
                         .Select(pair => pair.Key)
                         .ToArray())
                _tracks.Remove(stale);
        }
    }

    public IReadOnlyList<ChampionObservationTrack> Snapshot(
        DateTimeOffset now,
        TimeSpan? maximumAge = null)
    {
        var maxAge = maximumAge ?? TimeSpan.FromSeconds(90);
        lock (_sync)
        {
            return _tracks.Values
                .Where(item => now - item.LastSeenUtc <= maxAge)
                .OrderBy(item => item.Team)
                .ThenBy(item => item.Champion)
                .ToArray();
        }
    }

    public void Reset()
    {
        lock (_sync)
            _tracks.Clear();
    }

    private static string Key(string team, string champion) =>
        team.Trim().ToLowerInvariant() + ":" +
        champion.Trim().ToLowerInvariant();
}
