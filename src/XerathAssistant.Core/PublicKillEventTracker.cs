using System.Text.Json;

namespace XerathAssistant.Core;

/// <summary>
/// Reads only publicly announced, completed events from the local Live Client API.
/// A ChampionKill is NOT proof of a teamfight, and the API event contains no lane
/// or jungler position. First snapshot is ignored to avoid replaying old kills.
/// </summary>
public sealed class PublicKillEventTracker
{
    private bool _initialized;
    private int _lastEventId = -1;
    private double _lastGameTime = -1;

    public string? Observe(string eventsJson, double gameTime)
    {
        if (!double.IsFinite(gameTime) || gameTime < 0)
            throw new ArgumentOutOfRangeException(nameof(gameTime));

        using var doc = JsonDocument.Parse(eventsJson);
        var source = doc.RootElement.GetProperty("Events");
        if (source.ValueKind != JsonValueKind.Array) throw new FormatException("Events must be an array.");

        var seenIds = new HashSet<int>();
        var maxId = -1;
        var recentKills = 0;

        foreach (var e in source.EnumerateArray())
        {
            if (!e.TryGetProperty("EventID", out var idElement) ||
                !idElement.TryGetInt32(out var id) || id < 0)
                continue;
            if (!seenIds.Add(id)) continue;
            maxId = Math.Max(maxId, id);
            if (_initialized && id > _lastEventId &&
                e.TryGetProperty("EventName", out var name) &&
                name.ValueKind == JsonValueKind.String &&
                name.GetString() == "ChampionKill" &&
                e.TryGetProperty("EventTime", out var eventTime) &&
                eventTime.TryGetDouble(out var when) &&
                double.IsFinite(when) &&
                when >= 0 && when <= gameTime + 2 && gameTime - when <= 12)
                recentKills++;
        }

        if (_lastGameTime >= 0 && (gameTime + 3 < _lastGameTime ||
            (maxId >= 0 && maxId < _lastEventId && gameTime <= _lastGameTime)))
            _initialized = false;
        _lastGameTime = gameTime;
        if (!_initialized)
        {
            _initialized = true;
            _lastEventId = maxId;
            return null;
        }

        _lastEventId = Math.Max(_lastEventId, maxId);
        if (recentKills == 0) return null;
        return recentKills == 1
            ? "Vừa có một điểm hạ gục trên bản đồ. Kiểm tra thông báo trong game."
            : $"Vừa có {recentKills} điểm hạ gục trên bản đồ. Kiểm tra thông báo trong game.";
    }
}
