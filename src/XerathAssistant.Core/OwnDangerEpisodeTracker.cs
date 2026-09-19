namespace XerathAssistant.Core;

/// <summary>
/// Groups consecutive, verified OWN-HP damage samples into one observed episode.
/// Only an initial verified warning or a genuine severity escalation is announced.
/// Stable or healing samples close an episode without claiming that the player is safe.
/// Uses no screen image, enemy location, combat inference, or predictive model.
/// </summary>
public sealed class OwnDangerEpisodeTracker
{
    private SelfStatsSnapshot? _previous;
    private bool _active;
    private double _episodeStartHealth;
    private double _episodeMaxHealth;
    private double _lastDamageTime;
    private double _lastObservedTime = -1;
    private double _episodeLowestHealthPercent;
    private OwnDangerSeverity _peakSeverity;
    private OwnDangerSeverity _announcedSeverity;

    public bool IsActive => _active;
    public int EpisodeCount { get; private set; }
    public int CriticalEpisodeCount { get; private set; }
    public int CompletedEpisodeCount { get; private set; }
    public double GreatestEpisodeHealthLossPercent { get; private set; }
    public double LowestObservedEpisodeHealthPercent { get; private set; } = 100;

    /// <summary>
    /// The raw analyzer runs separately and supplies at most one notice per sample.
    /// Returns a notice only on a new episode or its first escalation to a higher
    /// severity. It deliberately emits NO "all clear" notification.
    /// </summary>
    public OwnDangerNotice? Observe(SelfStatsSnapshot current, OwnDangerNotice? candidate)
    {
        if (_lastObservedTime >= 0 && current.GameTimeSeconds + 3 < _lastObservedTime)
        {
            Reset();
            _previous = current.Health > 0 ? current : null;
            _lastObservedTime = current.GameTimeSeconds;
            return null;
        }

        if (_lastObservedTime >= 0 && current.GameTimeSeconds <= _lastObservedTime)
            return null; // duplicate/out-of-order API samples must never cause a transition.

        _lastObservedTime = current.GameTimeSeconds;
        var previous = _previous;
        _previous = current.Health > 0 ? current : null;

        if (current.Health <= 0 || current.MaxHealth <= 0)
        {
            ResetBaseline();
            return null;
        }

        var dt = previous is null ? double.PositiveInfinity :
            current.GameTimeSeconds - previous.GameTimeSeconds;
        if (previous is null || previous.Health <= 0 || dt > 2.5)
        {
            CloseEpisode();
            return null; // no conclusion about what happened in a sampling gap.
        }

        var loss = previous.Health - current.Health;
        if (_active)
        {
            _episodeLowestHealthPercent = Math.Min(_episodeLowestHealthPercent,
                current.HealthPercent);
            LowestObservedEpisodeHealthPercent = Math.Min(
                LowestObservedEpisodeHealthPercent, current.HealthPercent);
            if (loss > 0.1) _lastDamageTime = current.GameTimeSeconds;
            GreatestEpisodeHealthLossPercent = Math.Max(
                GreatestEpisodeHealthLossPercent,
                100d * Math.Max(0, _episodeStartHealth - current.Health) / _episodeMaxHealth);
            // Eight seconds without a fresh observed loss ends a recorded episode.
            // Stable low HP is NOT proof that the player is now safe.
            if (current.GameTimeSeconds - _lastDamageTime >= 8)
                CloseEpisode();
        }

        if (candidate is null || candidate.GameTimeSeconds != current.GameTimeSeconds ||
            loss <= 0 || dt <= 0)
            return null;

        if (!_active)
        {
            _active = true;
            EpisodeCount++;
            _episodeStartHealth = previous.Health;
            _episodeMaxHealth = current.MaxHealth;
            _episodeLowestHealthPercent = current.HealthPercent;
            _lastDamageTime = current.GameTimeSeconds;
            _peakSeverity = candidate.Severity;
            _announcedSeverity = candidate.Severity;
            LowestObservedEpisodeHealthPercent = Math.Min(
                LowestObservedEpisodeHealthPercent, current.HealthPercent);
            GreatestEpisodeHealthLossPercent = Math.Max(
                GreatestEpisodeHealthLossPercent,
                100d * Math.Max(0, _episodeStartHealth - current.Health) / _episodeMaxHealth);
            if (candidate.Severity == OwnDangerSeverity.Critical) CriticalEpisodeCount++;
            return candidate;
        }

        if (candidate.Severity > _peakSeverity)
        {
            _peakSeverity = candidate.Severity;
            if (candidate.Severity == OwnDangerSeverity.Critical) CriticalEpisodeCount++;
        }
        if (candidate.Severity <= _announcedSeverity)
            return null; // don't repeat an episode just because cooldown elapsed.

        _announcedSeverity = candidate.Severity;
        return candidate;
    }

    public void ResetBaseline()
    {
        CloseEpisode();
        _previous = null;
        _lastObservedTime = -1;
    }

    private void CloseEpisode()
    {
        if (_active) CompletedEpisodeCount++;
        _active = false;
        _episodeStartHealth = 0;
        _episodeMaxHealth = 0;
        _lastDamageTime = 0;
        _episodeLowestHealthPercent = 100;
        _peakSeverity = default;
        _announcedSeverity = default;
    }

    public void Reset()
    {
        ResetBaseline();
        EpisodeCount = 0;
        CriticalEpisodeCount = 0;
        CompletedEpisodeCount = 0;
        GreatestEpisodeHealthLossPercent = 0;
        LowestObservedEpisodeHealthPercent = 100;
    }
}
