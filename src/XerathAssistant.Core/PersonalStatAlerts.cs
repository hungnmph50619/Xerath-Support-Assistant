namespace XerathAssistant.Core;

/// <summary>
/// Opt-in personal-stat threshold alerts; hysteresis prevents a message every poll.
/// Uses only a player's own already visible stats; never infers map or opponent state.
/// </summary>
public sealed class PersonalStatAlerts
{
    private bool _initialized;
    private bool _healthLow;
    private bool _manaLow;
    private bool _goldHigh;
    private double _lastGameTime = -1;

    public int HealthThresholdPercent { get; }
    public int ManaThresholdPercent { get; }
    public double GoldThreshold { get; }

    public PersonalStatAlerts(int healthThresholdPercent = 30, int manaThresholdPercent = 25,
        double goldThreshold = 2500)
    {
        if (healthThresholdPercent is < 1 or > 95 || manaThresholdPercent is < 1 or > 95 ||
            !double.IsFinite(goldThreshold) || goldThreshold < 0)
            throw new ArgumentOutOfRangeException(nameof(healthThresholdPercent));
        HealthThresholdPercent = healthThresholdPercent;
        ManaThresholdPercent = manaThresholdPercent;
        GoldThreshold = goldThreshold;
    }

    public IReadOnlyList<string> Observe(SelfStatsSnapshot snapshot)
    {
        if (_lastGameTime >= 0 && snapshot.GameTimeSeconds + 20 < _lastGameTime)
            Reset();
        _lastGameTime = snapshot.GameTimeSeconds;
        var hpLow = snapshot.HealthPercent < HealthThresholdPercent;
        var manaLow = snapshot.MaxResource > 0 &&
            snapshot.ResourceType.Equals("MANA", StringComparison.OrdinalIgnoreCase) &&
            snapshot.ResourcePercent < ManaThresholdPercent;
        var goldHigh = snapshot.Gold >= GoldThreshold;

        // At startup, show stats but do not trigger a barrage of historical alerts.
        if (!_initialized)
        {
            _initialized = true;
            _healthLow = hpLow;
            _manaLow = manaLow;
            _goldHigh = goldHigh;
            return Array.Empty<string>();
        }

        var alerts = new List<string>();
        if (hpLow && !_healthLow)
            alerts.Add($"Máu của bạn vừa xuống dưới {HealthThresholdPercent}%.");
        if (manaLow && !_manaLow)
            alerts.Add($"Năng lượng của bạn vừa xuống dưới {ManaThresholdPercent}%.");
        if (goldHigh && !_goldHigh)
            alerts.Add($"Vàng hiện có vừa đạt {GoldThreshold:0}. Hãy kiểm tra trang bị khi thuận tiện.");

        // Re-arm only after stats move clearly away from the threshold.
        _healthLow = hpLow || (_healthLow && snapshot.HealthPercent <= HealthThresholdPercent + 5);
        _manaLow = manaLow || (_manaLow && snapshot.ResourcePercent <= ManaThresholdPercent + 5);
        _goldHigh = goldHigh || (_goldHigh && snapshot.Gold >= GoldThreshold * 0.9);
        return alerts;
    }

    public void Reset()
    {
        _initialized = false;
        _healthLow = false;
        _manaLow = false;
        _goldHigh = false;
        _lastGameTime = -1;
    }
}
