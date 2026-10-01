namespace XerathAssistant.Core;

public sealed record HudStateObservation(
    DateTimeOffset ObservedAt,
    double? HealthPercent,
    double? ManaPercent,
    int? Level,
    int? Gold,
    IReadOnlyList<AbilityHudState> Abilities,
    IReadOnlyList<ItemHudState> Items)
{
    public bool IsValid =>
        ObservedAt != default &&
        ValidPercent(HealthPercent) &&
        ValidPercent(ManaPercent) &&
        (Level is null or >= 1 and <= 18) &&
        Gold is null or >= 0 &&
        Abilities.All(x => x.IsValid) &&
        Items.All(x => x.IsValid);

    private static bool ValidPercent(double? value) =>
        value is null || double.IsFinite(value.Value) && value is >= 0 and <= 100;
}

public sealed record AbilityHudState(string Slot, bool? Ready, double? CooldownSeconds, int? Rank)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Slot) &&
        CooldownSeconds is null or >= 0 &&
        Rank is null or >= 0 and <= 5;
}

public sealed record ItemHudState(int Slot, string? ItemId, bool? Ready, double? CooldownSeconds)
{
    public bool IsValid => Slot is >= 0 and <= 6 && CooldownSeconds is null or >= 0;
}

public static class HudStateVision
{
    public static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(5);

    public static HudStateObservation? Accept(HudStateObservation observation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!observation.IsValid) return null;

        var age = now - observation.ObservedAt;
        if (age < TimeSpan.Zero || age > MaximumObservationAge) return null;

        return observation with
        {
            Abilities = observation.Abilities.OrderBy(x => x.Slot, StringComparer.OrdinalIgnoreCase).ToArray(),
            Items = observation.Items.OrderBy(x => x.Slot).ToArray()
        };
    }
}
