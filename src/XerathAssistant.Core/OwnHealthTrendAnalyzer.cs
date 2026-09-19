namespace XerathAssistant.Core;

/// <summary>
/// Aggregates genuine OWN-health decreases over a short, contiguous game-clock
/// window. Unlike one-sample burst alerts, this detects two or more smaller hits.
/// Nothing here inspects an opponent, the minimap, or forecasts future danger.
/// </summary>
public sealed class OwnHealthTrendAnalyzer
{
    private const double WindowSeconds = 4;
    private readonly Queue<SelfStatsSnapshot> _samples = new();
    private double _lastGameTime = -1;

    public OwnDangerNotice? Observe(SelfStatsSnapshot current)
    {
        if (_lastGameTime >= 0 && current.GameTimeSeconds + 3 < _lastGameTime)
            Reset();
        else if (_lastGameTime >= 0 && current.GameTimeSeconds <= _lastGameTime)
            return null;

        if (_lastGameTime >= 0 && current.GameTimeSeconds - _lastGameTime > 2.5)
            _samples.Clear();
        _lastGameTime = current.GameTimeSeconds;

        if (current.Health <= 0 || current.MaxHealth <= 0)
        {
            _samples.Clear();
            return null;
        }

        if (_samples.Count > 0)
        {
            var previous = _samples.Last();
            // Changing maximum HP can distort percent comparisons. Only one
            // comparable contiguous segment is accepted per rolling window.
            if (Math.Abs(previous.MaxHealth - current.MaxHealth) >
                Math.Max(10, previous.MaxHealth * .10))
                _samples.Clear();
        }
        _samples.Enqueue(current);
        while (_samples.Count > 0 &&
               current.GameTimeSeconds - _samples.Peek().GameTimeSeconds > WindowSeconds)
            _samples.Dequeue();

        if (_samples.Count < 3) return null;
        var values = _samples.ToArray();
        var damagingSteps = 0;
        for (var i = 1; i < values.Length; i++)
            if (values[i - 1].Health - values[i].Health >= current.MaxHealth * .015)
                damagingSteps++;
        if (damagingSteps < 2) return null;

        var first = values[0];
        var elapsed = current.GameTimeSeconds - first.GameTimeSeconds;
        var loss = first.Health - current.Health;
        var lossPercent = 100d * loss / current.MaxHealth;
        if (elapsed <= 0 || lossPercent < 22) return null;

        var remaining = Math.Clamp(current.HealthPercent, 0, 100);
        var severity = remaining <= 20 ? OwnDangerSeverity.Critical :
                       remaining <= 40 ? OwnDangerSeverity.High :
                       OwnDangerSeverity.Elevated;
        var message = severity switch
        {
            OwnDangerSeverity.Critical =>
                $"Nguy hiểm: bạn vừa mất {lossPercent:0}% máu qua nhiều lần giảm trong {elapsed:0.#} giây, chỉ còn {remaining:0}%.",
            OwnDangerSeverity.High =>
                $"Cảnh báo: bạn vừa mất {lossPercent:0}% máu qua nhiều lần giảm trong {elapsed:0.#} giây, còn {remaining:0}%.",
            _ =>
                $"Máu giảm liên tiếp: đã mất {lossPercent:0}% trong {elapsed:0.#} giây, còn {remaining:0}%."
        };
        return new OwnDangerNotice(severity, message, current.GameTimeSeconds,
            remaining, lossPercent);
    }

    public void Reset() { _samples.Clear(); _lastGameTime = -1; }
}
