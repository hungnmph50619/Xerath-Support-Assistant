using System.Text.Json;

namespace XerathAssistant.Core;

/// <summary>
/// Own publicly visible game stats only. No enemy info, hidden map state or strategic recommendation.
/// </summary>
public sealed record SelfStatsSnapshot(
    double GameTimeSeconds,
    int Level,
    double Health,
    double MaxHealth,
    double Resource,
    double MaxResource,
    string ResourceType,
    double Gold,
    double AbilityPower)
{
    public double HealthPercent => MaxHealth > 0 ? 100d * Health / MaxHealth : 0d;
    public double ResourcePercent => MaxResource > 0 ? 100d * Resource / MaxResource : 0d;
    public static string Clock(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"mm\:ss");
}

/// <summary>Validates local Live Client Data API responses. Throws on partial/corrupt values.</summary>
public static class SelfStatsParser
{
    public static SelfStatsSnapshot Parse(string activePlayerJson, string gameStatsJson)
    {
        using var activeDoc = JsonDocument.Parse(activePlayerJson);
        using var gameDoc = JsonDocument.Parse(gameStatsJson);
        var root = activeDoc.RootElement;
        var stats = root.GetProperty("championStats");
        var time = ReadNumber(gameDoc.RootElement, "gameTime");
        var level = root.GetProperty("level").GetInt32();
        var health = ReadNumber(stats, "currentHealth");
        var maxHealth = ReadNumber(stats, "maxHealth");
        var resource = ReadNumber(stats, "resourceValue");
        var maxResource = ReadNumber(stats, "resourceMax");
        var gold = ReadNumber(root, "currentGold");
        var ap = ReadNumber(stats, "abilityPower");
        var type = stats.GetProperty("resourceType").GetString() ?? "";

        if (time < 0 || level is < 1 or > 30 || maxHealth <= 0 ||
            health < 0 || health > maxHealth * 1.1 || resource < 0 ||
            maxResource < 0 || resource > Math.Max(1, maxResource) * 1.1 ||
            gold < 0 || ap < 0)
            throw new FormatException("Thông số nhân vật không hợp lệ hoặc chưa sẵn sàng.");
        return new SelfStatsSnapshot(time, level, health, maxHealth, resource,
            maxResource, type, gold, ap);
    }

    private static double ReadNumber(JsonElement parent, string property)
    {
        var value = parent.GetProperty(property).GetDouble();
        if (!double.IsFinite(value)) throw new FormatException("Thông số không hợp lệ.");
        return value;
    }
}

/// <summary>A local session summary, computed from sparse own-player observations only.</summary>
public sealed class SelfStatsSession
{
    private double _lastTime = -1;
    public int Samples { get; private set; }
    public double LowestHealthPercent { get; private set; } = 100;
    public double LowestResourcePercent { get; private set; } = 100;
    public double HighestObservedGold { get; private set; }
    public double LowResourceObservedSeconds { get; private set; }

    public void Add(SelfStatsSnapshot sample)
    {
        // Game clock resets for a new game; discard the prior game's samples.
        if (_lastTime >= 0 && sample.GameTimeSeconds + 20 < _lastTime) Reset();
        var dt = _lastTime >= 0 ? Math.Clamp(sample.GameTimeSeconds - _lastTime, 0, 10) : 0;
        Samples++;
        LowestHealthPercent = Math.Min(LowestHealthPercent, sample.HealthPercent);
        if (sample.MaxResource > 0 && sample.ResourceType.Equals("MANA", StringComparison.OrdinalIgnoreCase))
        {
            LowestResourcePercent = Math.Min(LowestResourcePercent, sample.ResourcePercent);
            if (sample.ResourcePercent < 25) LowResourceObservedSeconds += dt;
        }
        HighestObservedGold = Math.Max(HighestObservedGold, sample.Gold);
        _lastTime = sample.GameTimeSeconds;
    }

    public void Reset()
    {
        _lastTime = -1;
        Samples = 0;
        LowestHealthPercent = 100;
        LowestResourcePercent = 100;
        HighestObservedGold = 0;
        LowResourceObservedSeconds = 0;
    }
}
