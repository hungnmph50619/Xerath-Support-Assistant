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

var unknownScenario = new WaveFightScenario(
    WaveLocation.Unknown, WaveDirection.Unknown, AdcPlan.Unknown,
    Condition.Unknown, Condition.Unknown, VisionState.Unknown,
    JungleState.Unknown, AllyState.Unknown, NumbersState.Unknown,
    EnemyEngageState.Unknown, MinionState.Unknown, ObjectiveState.Unknown);
var unknownAdvice = WaveFightAdvisor.Evaluate(unknownScenario);
Verify(unknownAdvice.Wave.State == AdviceState.InsufficientInformation &&
       unknownAdvice.Position.State == AdviceState.InsufficientInformation &&
       unknownAdvice.Fight.State == AdviceState.InsufficientInformation,
       "offline advisor refuses decisions with missing observations");
Verify(unknownAdvice.Fight.Missing.Contains("Thông tin rừng địch"),
       "unknown jungle position explicitly requires more information");

var scenario = new WaveFightScenario(
    WaveLocation.EnemyHalf, WaveDirection.TowardEnemy, AdcPlan.CrashThenRecall,
    Condition.Healthy, Condition.Healthy, VisionState.Controlled,
    JungleState.RecentlySeenTop, AllyState.Together, NumbersState.Advantage,
    EnemyEngageState.RecentlyUsed, MinionState.Even, ObjectiveState.NotSoon);
var conditional = WaveFightAdvisor.Evaluate(scenario);
Verify(conditional.Wave.State == AdviceState.ConditionalOpportunity &&
       conditional.Fight.State == AdviceState.ConditionalOpportunity,
       "favorable manually entered scenario yields conditional options, not guarantees");
Verify(conditional.Position.State == AdviceState.Evaluate &&
       conditional.Position.Explanation.Contains("độ mới"),
       "enemy-half position still warns about stale jungler location");
Verify(WaveFightAdvisor.Evaluate(scenario with { AdcPlan = AdcPlan.Hold }).Wave.State ==
       AdviceState.Caution, "ADC hold intention prevents fast-push suggestion");
Verify(WaveFightAdvisor.Evaluate(scenario with {
    WaveLocation = WaveLocation.AllyHalf, WaveDirection = WaveDirection.TowardAlly
}).Wave.State == AdviceState.Caution,
       "wave pushing toward allied tower gets wave preservation guidance");
Verify(WaveFightAdvisor.Evaluate(scenario with { Vision = VisionState.Dark }).Fight.State ==
       AdviceState.Caution, "dark river vision blocks aggressive fight guidance");
Verify(WaveFightAdvisor.Evaluate(scenario with { EnemyJungle = JungleState.NoRecentSighting }).Position.State ==
       AdviceState.Caution, "old or unavailable jungle information cannot imply safety");
Verify(WaveFightAdvisor.Evaluate(scenario with { NearbyNumbers = NumbersState.Unknown }).Fight.State ==
       AdviceState.InsufficientInformation, "unknown player count blocks fight evaluation");
Verify(WaveFightAdvisor.Evaluate(scenario with { MinionPressure = MinionState.EnemyLarge }).Fight.State ==
       AdviceState.Caution, "large enemy wave is a fight risk");
Verify(WaveFightAdvisor.Evaluate(scenario with { Objective = ObjectiveState.Soon }).Wave.State ==
       AdviceState.Caution, "upcoming objective blocks automatic push-and-recall suggestion");
Verify(WaveFightAdvisor.Evaluate(scenario with { Objective = ObjectiveState.Unknown }).Wave.State ==
       AdviceState.InsufficientInformation, "missing objective timing blocks recall evaluation");
Verify(WaveFightAdvisor.Evaluate(scenario with { Health = Condition.Low }).Fight.State ==
       AdviceState.Caution, "low health blocks favorable fight judgment");


const string ownJson = """
{"level":8,"currentGold":2304,"championStats":{"currentHealth":1164,"maxHealth":1265,"resourceValue":527,"resourceMax":527,"resourceType":"MANA","abilityPower":124}}
""";
const string gameJson = """{"gameTime":560.0}""";
var own = SelfStatsParser.Parse(ownJson, gameJson);
Verify(own.Level == 8 && own.Gold == 2304 && own.Health == 1164,
       "read own level, gold and health from Riot sample-shaped JSON");
Verify(Math.Abs(own.HealthPercent - (1164d / 1265d * 100d)) < 0.001 &&
       own.ResourcePercent == 100 && SelfStatsSnapshot.Clock(560) == "09:20" &&
       SelfStatsSnapshot.Clock(3660) == "61:00",
       "self stats percentages and long game clock");
var invalidSelfData = false;
try { SelfStatsParser.Parse(ownJson.Replace("\"currentHealth\":1164", "\"currentHealth\":-5"), gameJson); }
catch (FormatException) { invalidSelfData = true; }
Verify(invalidSelfData, "reject invalid self-health values");
var personalSession = new SelfStatsSession();
personalSession.Add(own);
personalSession.Add(own with { GameTimeSeconds = 565, Resource = 100, Health = 450, Gold = 2600 });
personalSession.Add(own with { GameTimeSeconds = 570, Resource = 90, Health = 400, Gold = 500 });
Verify(personalSession.Samples == 3 && personalSession.LowResourceObservedSeconds == 10 &&
       personalSession.HighestObservedGold == 2600 && personalSession.LowestHealthPercent < 35,
       "summarize observed resource shortages, health and peak carried gold");
personalSession.Add(own with { GameTimeSeconds = 1 });
Verify(personalSession.Samples == 1 && personalSession.LowResourceObservedSeconds == 0,
       "new match resets previous in-memory summary");

Console.WriteLine($"ALL {count} CORE TESTS PASSED");
