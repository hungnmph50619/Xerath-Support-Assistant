namespace XerathAssistant.Core;

/// <summary>
/// A manually annotated last-seen point in an already saved screenshot. No live game input.
/// Normalized X/Y refer to the full image, not a guessed live map location.
/// </summary>
public sealed record LastSeenMark(double X, double Y, TimeSpan MatchTime, string Note);

public sealed class LastSeenReview
{
    public LastSeenMark? Current { get; private set; }

    public void Mark(double x, double y, TimeSpan matchTime, string? note = null)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Coordinates must be normalized between 0 and 1.");
        if (matchTime < TimeSpan.Zero || matchTime >= TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(matchTime));
        Current = new LastSeenMark(x, y, matchTime, (note ?? "").Trim());
    }

    public void Clear() => Current = null;

    /// <summary>Only compute elapsed game time when the user supplied a later replay time.</summary>
    public TimeSpan? AgeAt(TimeSpan reviewTime) =>
        Current is { } mark && reviewTime >= mark.MatchTime
            ? reviewTime - mark.MatchTime
            : null;

    public static bool TryParseGameTime(string? value, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split(':');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 3 ||
            parts[1].Length != 2 || !parts.All(part => part.All(char.IsDigit)) ||
            !int.TryParse(parts[0], out var minutes) ||
            !int.TryParse(parts[1], out var seconds) ||
            minutes > 1439 || seconds > 59)
            return false;
        time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        return true;
    }

    public static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
}
