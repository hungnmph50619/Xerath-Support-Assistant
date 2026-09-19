namespace XerathAssistant.Core;

/// <summary>
/// Factual, own-player danger observed from TWO fresh, consecutive Live Client
/// snapshots. Never diagnoses an enemy, a gank, an incoming spell or a future death.
/// The severity is a local UI priority, not a prediction of match outcome.
/// </summary>
public enum OwnDangerSeverity { Elevated = 1, High = 2, Critical = 3 }

public sealed record OwnDangerNotice(
    OwnDangerSeverity Severity,
    string Message,
    double GameTimeSeconds,
    double HealthPercent,
    double ObservedLossPercent);

public sealed class OwnDangerAnalyzer
{
    private SelfStatsSnapshot? _previous;
    private double _lastSampleTime = -1;
    private double _lastAlertTime = double.NegativeInfinity;
    private double _lastAlertHealth = double.PositiveInfinity;
    private OwnDangerSeverity _lastSeverity;
    public int ObservedDangerEpisodes { get; private set; }
    public int ObservedCriticalEpisodes { get; private set; }
    public double GreatestObservedLossPercent { get; private set; }

    /// <summary>
    /// Do not alert from an initial low-health sample or from stale, duplicate,
    /// out-of-order, respawn or dead snapshots. Each alert quotes observed values.
    /// </summary>
    public OwnDangerNotice? Observe(SelfStatsSnapshot current)
    {
        if (_lastSampleTime >= 0 && current.GameTimeSeconds + 3 < _lastSampleTime)
            Reset();
        _lastSampleTime = current.GameTimeSeconds;

        if (current.Health <= 0 || current.MaxHealth <= 0)
        {
            _previous = null;
            return null;
        }

        var previous = _previous;
        _previous = current;
        if (previous is null || previous.Health <= 0 || previous.MaxHealth <= 0)
            return null;

        var dt = current.GameTimeSeconds - previous.GameTimeSeconds;
        if (dt <= 0 || dt > 2.5 || !double.IsFinite(dt))
            return null;

        // Compare in raw HP; a max-health increase should never be interpreted
        // as damage. The public API occasionally reports duplicate timestamps.
        var absoluteLoss = previous.Health - current.Health;
        if (absoluteLoss <= 0) return null;
        var lossPercent = 100d * absoluteLoss / current.MaxHealth;
        var remaining = Math.Clamp(current.HealthPercent, 0, 100);
        OwnDangerSeverity? severity =
            remaining <= 20 && lossPercent >= 5 ? OwnDangerSeverity.Critical :
            remaining <= 40 && lossPercent >= 8 ? OwnDangerSeverity.High :
            remaining <= 65 && lossPercent >= 22 ? OwnDangerSeverity.Elevated :
            lossPercent >= 35 ? OwnDangerSeverity.Elevated : null;

        if (severity is null) return null;
        var escalate = severity.Value > _lastSeverity;
        // New major hit can be announced even before cooldown if significantly
        // more HP was lost since the last warning; don't flood on poll repeats.
        var furtherDamage = _lastAlertHealth - current.Health >= current.MaxHealth * .16;
        if (current.GameTimeSeconds - _lastAlertTime < 8 && !escalate && !furtherDamage)
            return null;

        _lastAlertTime = current.GameTimeSeconds;
        _lastAlertHealth = current.Health;
        _lastSeverity = severity.Value;
        ObservedDangerEpisodes++;
        if (severity == OwnDangerSeverity.Critical) ObservedCriticalEpisodes++;
        GreatestObservedLossPercent = Math.Max(GreatestObservedLossPercent, lossPercent);

        // Wording is not a claim that an opponent is near, or that HP will
        // continue falling; this is a concrete retrospective observation.
        var message = severity switch
        {
            OwnDangerSeverity.Critical =>
                $"Nguy hiểm: bạn chỉ còn {remaining:0}% máu, vừa mất {lossPercent:0}% máu trong {dt:0.#} giây.",
            OwnDangerSeverity.High =>
                $"Cảnh báo: bạn còn {remaining:0}% máu, vừa mất {lossPercent:0}% máu trong {dt:0.#} giây.",
            _ =>
                $"Máu giảm mạnh: vừa mất {lossPercent:0}% trong {dt:0.#} giây; hiện còn {remaining:0}%."
        };
        return new OwnDangerNotice(severity.Value, message, current.GameTimeSeconds,
            remaining, lossPercent);
    }

    /// <summary>Clear temporal comparisons on death/respawn; retain summary counts.</summary>
    public void ResetBaseline()
    {
        _previous = null;
        _lastAlertTime = double.NegativeInfinity;
        _lastAlertHealth = double.PositiveInfinity;
        _lastSeverity = default;
    }

    public void Reset()
    {
        ResetBaseline();
        _lastSampleTime = -1;
        ObservedDangerEpisodes = 0;
        ObservedCriticalEpisodes = 0;
        GreatestObservedLossPercent = 0;
    }
}
