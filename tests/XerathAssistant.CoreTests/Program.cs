using System.Numerics;
using XerathAssistant.Core;

var count = 0;
void Verify(bool condition, string label)
{
    if (!condition) throw new Exception($"FAILED: {label}");
    Console.WriteLine($"PASS: {label}");
    count++;
}

var origin = Vector2.Zero;
var stationary = new TargetObservation(new Vector2(300f, 0f), Vector2.Zero);
var moving = new TargetObservation(new Vector2(300f, 0f), new Vector2(40f, 0f));
var qShort = AimEngine.Predict(new AimRequest(origin, stationary, Spell.Q, 0f));
var qLong = AimEngine.Predict(new AimRequest(origin, stationary, Spell.Q, 1.5f));
Verify(qLong.Range > qShort.Range, "Q charge increases practice range");
Verify(qLong.InRange, "stationary target in Q range");
var w = AimEngine.Predict(new AimRequest(origin, moving, Spell.W));
Verify(MathF.Abs(w.AimPoint.X - 330f) < .01f, "W leads moving target by delay");
var e = AimEngine.Predict(new AimRequest(origin, moving, Spell.E));
Verify(e.TimeToImpact > .16f && e.AimPoint.X > 300f, "E intercept includes windup and travel");
Verify(AimEngine.TryIntercept(origin, new Vector2(100, 0), Vector2.Zero, 100f, out var t)
       && Math.Abs(t - 1f) < .0001f, "intercept stationary target");
Verify(!AimEngine.TryIntercept(origin, new Vector2(100, 0), new Vector2(200, 0), 100f, out _),
       "no intercept when target runs away faster than projectile");
Verify(AimEngine.IsHit(Spell.W, origin, new Vector2(330, 0), new Vector2(338, 0),
       650f, 49f), "W circular hit test");
Verify(!AimEngine.IsHit(Spell.W, origin, new Vector2(330, 0), new Vector2(400, 0),
       650f, 49f), "W circular miss test");
Verify(AimEngine.DistanceToSegment(new Vector2(50f, 10f), origin,
       new Vector2(100f, 0f)) < 10.001f, "distance to projectile path");
Verify(!AimEngine.IsHit(Spell.E, origin, new Vector2(300, 0), new Vector2(300, 0),
       605f, 19f, new Vector2(120, 0)), "minion blocks E");
Verify(!AimEngine.Predict(new AimRequest(origin,
       new TargetObservation(new Vector2(900, 0), Vector2.Zero), Spell.W)).InRange,
       "out-of-range rejection");

var reminder = new ReminderEngine(TimeSpan.FromSeconds(30));
reminder.Start(new[]
{
    new ReminderItem("map", "Nhìn minimap.", TimeSpan.FromSeconds(45)),
    new ReminderItem("jungle", "Kiểm tra rừng đồng minh.", TimeSpan.FromSeconds(90))
});
Verify(reminder.Tick(TimeSpan.FromSeconds(44)) is null, "no reminder before due");
Verify(reminder.Tick(TimeSpan.FromSeconds(45)) == "Nhìn minimap.", "first reminder on time");
Verify(reminder.Tick(TimeSpan.FromSeconds(60)) is null, "suppress alerts inside 30-second gap");
Verify(reminder.Tick(TimeSpan.FromSeconds(90)) is string at90 &&
       at90.Contains("minimap") && at90.Contains("rừng"),
       "combine reminders due at same time");
Verify(reminder.Tick(TimeSpan.FromSeconds(91)) is null, "no duplicate after combined alert");
reminder.Start(new[] { new ReminderItem("vision", "Kiểm tra mắt.", TimeSpan.FromSeconds(5)) });
Verify(reminder.Tick(TimeSpan.FromSeconds(5)) == "Kiểm tra mắt.", "restart resets previous session");
Verify(reminder.Tick(TimeSpan.FromSeconds(10)) is null, "global suppression applies to rapid reminder");
Verify(reminder.Tick(TimeSpan.FromSeconds(35)) == "Kiểm tra mắt.", "deferred reminder fires after gap");
Console.WriteLine($"ALL {count} CORE TESTS PASSED");
