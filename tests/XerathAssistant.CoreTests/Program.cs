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
var review = new LastSeenReview();
Verify(LastSeenReview.TryParseGameTime("09:20", out var at920) &&
       at920 == TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(20),
       "parse historical match timestamp");
Verify(!LastSeenReview.TryParseGameTime("09:60", out _) &&
       !LastSeenReview.TryParseGameTime("later", out _),
       "reject invalid match times");
review.Mark(0.84, 0.75, at920, "Bụi sông dưới");
Verify(review.Current is { } first &&
       Math.Abs(first.X - 0.84) < 0.0001 &&
       first.MatchTime == at920, "manual normalized marker");
Verify(review.AgeAt(TimeSpan.FromMinutes(10)) == TimeSpan.FromSeconds(40),
       "elapsed time calculated from manually entered review timestamp");
Verify(review.AgeAt(TimeSpan.FromMinutes(8)) is null,
       "never imply a future observation has already occurred");
review.Mark(0.1, 0.2, TimeSpan.FromMinutes(11));
Verify(review.Current is { X: 0.1, Y: 0.2 } &&
       review.Current.MatchTime == TimeSpan.FromMinutes(11),
       "new sighting replaces the old marker");
review.Clear();
Verify(review.Current is null, "clear last-seen marker");
var invalidPositionRejected = false;
try { review.Mark(1.1, 0.5, TimeSpan.Zero); }
catch (ArgumentOutOfRangeException) { invalidPositionRejected = true; }
Verify(invalidPositionRejected, "reject off-image coordinates");
Console.WriteLine($"ALL {count} CORE TESTS PASSED");
