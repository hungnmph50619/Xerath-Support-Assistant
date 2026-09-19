namespace XerathAssistant.Core;

/// <summary>
/// Tracks only confirmed HP=0 and subsequent HP>0 of the active player.
/// Does not infer cause of death, enemy presence or respawn timer.
/// </summary>
public enum OwnLifeTransition { None, Died, Respawned }

public sealed class OwnLifeStateDetector
{
    private bool _initialized;
    private bool _dead;
    private double _lastGameTime = -1;

    public bool IsDead => _initialized && _dead;

    public OwnLifeTransition Observe(SelfStatsSnapshot snapshot)
    {
        if (_lastGameTime >= 0 && snapshot.GameTimeSeconds + 3 < _lastGameTime)
            Reset();
        _lastGameTime = snapshot.GameTimeSeconds;
        if (snapshot.MaxHealth <= 0) return OwnLifeTransition.None;

        var deadNow = snapshot.Health <= 0;
        if (!_initialized)
        {
            _initialized = true;
            _dead = deadNow;
            return deadNow ? OwnLifeTransition.Died : OwnLifeTransition.None;
        }

        if (deadNow == _dead) return OwnLifeTransition.None;
        _dead = deadNow;
        return deadNow ? OwnLifeTransition.Died : OwnLifeTransition.Respawned;
    }

    public void Reset()
    {
        _initialized = false;
        _dead = false;
        _lastGameTime = -1;
    }
}
