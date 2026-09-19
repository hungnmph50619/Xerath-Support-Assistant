namespace XerathAssistant.Core;

/// <summary>
/// A real-time, factual detector for changes in the active player's own health.
/// It does not observe enemy locations, infer a teamfight, or issue tactical orders.
/// </summary>
public sealed class OwnHealthChangeDetector
{
    private SelfStatsSnapshot? _last;
    private double _lastNoticeGameTime = double.NegativeInfinity;
    private double _lastNoticeHealth = double.PositiveInfinity;

    /// <summary>
    /// Returns an alert for a substantial loss of own HP observed in at most two
    /// game seconds. Cooldown prevents repeated alerts during a single damage burst.
    /// </summary>
    public string? Observe(SelfStatsSnapshot snapshot)
    {
        if (_last is not { } previous)
        {
            _last = snapshot;
            return null;
        }

        var elapsed = snapshot.GameTimeSeconds - previous.GameTimeSeconds;
        _last = snapshot;

        if (!double.IsFinite(elapsed) || elapsed < -3)
        {
            Reset();
            _last = snapshot;
            return null;
        }

        // The API may return duplicate game-clock samples. Ignore these rather
        // than suggesting two independent observations were made.
        if (elapsed <= 0 || elapsed > 2.5 || previous.MaxHealth <= 0 ||
            snapshot.MaxHealth <= 0 || snapshot.Health <= 0)
            return null;

        var loss = previous.Health - snapshot.Health;
        var fraction = loss / previous.MaxHealth;
        if (fraction < 0.22 || loss < 100)
            return null;

        // A second urgent alert may appear if HP falls another 30% of maximum,
        // otherwise wait eight game seconds before showing another.
        var additionalLoss = _lastNoticeHealth - snapshot.Health;
        if (snapshot.GameTimeSeconds - _lastNoticeGameTime < 8 &&
            additionalLoss < snapshot.MaxHealth * 0.30)
            return null;

        _lastNoticeGameTime = snapshot.GameTimeSeconds;
        _lastNoticeHealth = snapshot.Health;
        return $"Máu của bạn vừa giảm {Math.Round(fraction * 100):0}% trong {elapsed:0.#} giây (còn {snapshot.HealthPercent:0}%).";
    }

    public void Reset()
    {
        _last = null;
        _lastNoticeGameTime = double.NegativeInfinity;
        _lastNoticeHealth = double.PositiveInfinity;
    }
}
