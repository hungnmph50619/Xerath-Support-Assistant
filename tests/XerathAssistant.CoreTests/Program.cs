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


var statAlerts = new PersonalStatAlerts(30, 25, 2500);
Verify(statAlerts.Observe(own).Count == 0,
       "own-stat alerts do not replay historical thresholds on first observation");
var alertsOnCrossing = statAlerts.Observe(own with {
    GameTimeSeconds = 561, Health = 300, Resource = 110, Gold = 2550
});
Verify(alertsOnCrossing.Count == 3 && alertsOnCrossing.Any(a => a.Contains("Máu")) &&
       alertsOnCrossing.Any(a => a.Contains("Năng lượng")) &&
       alertsOnCrossing.Any(a => a.Contains("Vàng")),
       "own health mana and gold crossings produce factual warnings");
Verify(statAlerts.Observe(own with {
    GameTimeSeconds = 562, Health = 290, Resource = 90, Gold = 2600
}).Count == 0, "no alert spam while own stats remain below or above thresholds");
Verify(statAlerts.Observe(own with {
    GameTimeSeconds = 563, Health = 1200, Resource = 400, Gold = 2100
}).Count == 0, "recovery re-arms stat thresholds");
Verify(statAlerts.Observe(own with {
    GameTimeSeconds = 564, Health = 290, Resource = 100, Gold = 2600
}).Count == 3, "stat alerts can fire again after recovery and re-crossing");
Verify(statAlerts.Observe(own with {
    GameTimeSeconds = 1, Health = 290, Resource = 100, Gold = 2600
}).Count == 0, "new game restarts self-stat alert tracking without old alerts");

var killTracker = new PublicKillEventTracker();
const string openingEvents = """
{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0}]}
""";
const string oneKillEvents = """
{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0},{"EventID":1,"EventName":"ChampionKill","EventTime":13}]}
""";
Verify(killTracker.Observe(openingEvents, 12) is null,
       "initial event snapshot never replays historical announcements");
var killNotice = killTracker.Observe(oneKillEvents, 14);
Verify(killNotice is not null && killNotice.Contains("hạ gục") &&
       !killNotice.Contains("Mid") && !killNotice.Contains("Top") &&
       !killNotice.Contains("giao tranh"),
       "completed champion kill notice never invents fight or lane location");
Verify(killTracker.Observe(oneKillEvents, 15) is null,
       "same Riot event is never announced twice");
const string twoKillEvents = """
{"Events":[{"EventID":0,"EventName":"GameStart","EventTime":0},{"EventID":1,"EventName":"ChampionKill","EventTime":13},{"EventID":2,"EventName":"ChampionKill","EventTime":16},{"EventID":3,"EventName":"ChampionKill","EventTime":17}]}
""";
Verify(killTracker.Observe(twoKillEvents, 18) is string twoKills &&
       twoKills.Contains("2 điểm hạ gục"),
       "group freshly completed kill events without calling them a live teamfight");
Verify(killTracker.Observe(openingEvents, 0) is null,
       "new match game clock resets event stream without replaying old kills");
Verify(killTracker.Observe(oneKillEvents, 14) is string newMatchKill &&
       newMatchKill.Contains("hạ gục"),
       "new match can announce newly completed kills after event ID reset");


var healthDetector = new OwnHealthChangeDetector();
var healthBaseline = own with { GameTimeSeconds = 700, Health = 1000, MaxHealth = 1200 };
Verify(healthDetector.Observe(healthBaseline) is null,
       "real-time health-change monitor does not invent an alert from initial snapshot");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 701, Health = 900 }) is null,
       "ordinary health loss does not trigger a false emergency alert");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 702, Health = 590 })
           is string damageAlert && damageAlert.Contains("giảm") &&
           damageAlert.Contains("26%"),
       "large own-health drop produces an immediate factual damage alert");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 703, Health = 550 }) is null,
       "minor follow-up damage is suppressed during warning cooldown");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 704, Health = 170 }) is not null,
       "a second major burst can bypass cooldown when own health drops further");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 705, Health = 0 }) is null,
       "no spurious damage notification from zero health");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 2, Health = 1000 }) is null,
       "new game resets damage observations before any announcement");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 3, Health = 690 }) is not null,
       "new game detects genuine own-health loss independently");
Verify(healthDetector.Observe(healthBaseline with { GameTimeSeconds = 10, Health = 100 }) is null,
       "sampling gaps do not imply damage happened in the most recent two seconds");


var life = new OwnLifeStateDetector();
var lifeBase = own with { GameTimeSeconds = 900, Health = 1000, MaxHealth = 1200 };
Verify(life.Observe(lifeBase) == OwnLifeTransition.None && !life.IsDead,
       "initial alive state causes no fake respawn notice");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 901, Health = 0 })
       == OwnLifeTransition.Died && life.IsDead,
       "confirmed own HP zero triggers a single death transition");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 902, Health = 0 })
       == OwnLifeTransition.None && life.IsDead,
       "remaining dead never repeats a death announcement");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 912, Health = 1000 })
       == OwnLifeTransition.Respawned && !life.IsDead,
       "confirmed own HP recovery triggers one respawn announcement");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 913, Health = 1000 })
       == OwnLifeTransition.None,
       "continued living does not replay respawn");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 2, Health = 0 })
       == OwnLifeTransition.Died && life.IsDead,
       "game clock reset clears earlier life status before new match");
Verify(life.Observe(lifeBase with { GameTimeSeconds = 3, Health = 1000 })
       == OwnLifeTransition.Respawned && !life.IsDead,
       "new match life-state stream can independently recover");


var danger = new OwnDangerAnalyzer();
var safeBaseline = own with { GameTimeSeconds = 1200, Health = 900, MaxHealth = 1000 };
Verify(danger.Observe(safeBaseline with { Health = 190 }) is null,
       "initial low-health sample is not a fabricated immediate danger");
danger.Reset();
Verify(danger.Observe(safeBaseline) is null,
       "initial living sample builds danger baseline");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1201, Health = 860 }) is null,
       "minor verified damage does not trigger danger");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1202, Health = 580 })
           is { Severity: OwnDangerSeverity.Elevated } elevated &&
       elevated.Message.Contains("28%") && elevated.Message.Contains("58%"),
       "substantial observed damage while alive produces evidence-backed elevated warning");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1203, Health = 420 }) is null,
       "repeated burst does not flood during cooldown without larger damage");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1204, Health = 310 })
           is { Severity: OwnDangerSeverity.High } high &&
       high.Message.Contains("31%"),
       "confirmed loss below 40 percent escalates above earlier warning");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1205, Health = 100 })
           is { Severity: OwnDangerSeverity.Critical } critical &&
       critical.Message.Contains("10%"),
       "confirmed critical HP and fresh damage takes highest live priority");
Verify(danger.ObservedDangerEpisodes == 3 &&
       danger.ObservedCriticalEpisodes == 1 && danger.GreatestObservedLossPercent >= 28,
       "session summary counts only actual warning episodes and greatest observed one-second loss");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1206, Health = 0 }) is null,
       "own death is not misreported as danger warning");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1215, Health = 1000 }) is null,
       "respawn creates a fresh baseline without a synthetic risk");
danger.ResetBaseline();
Verify(danger.ObservedDangerEpisodes == 3 && danger.ObservedCriticalEpisodes == 1,
       "death/respawn baseline reset preserves per-match observed analysis");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1220, Health = 200 }) is null,
       "fresh baseline after life transition never interprets an old sample as damage");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1221, Health = 190 })
           is null, "low but steady HP does not invent an incoming threat");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1210, Health = 40 }) is null &&
       danger.ObservedDangerEpisodes == 0,
       "backwards match clock resets risk history and does not compare stale values");
danger.Reset();
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1300, Health = 950 }) is null &&
       danger.Observe(safeBaseline with { GameTimeSeconds = 1300, Health = 250 }) is null,
       "duplicated game time never triggers a danger warning");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1310, Health = 120 }) is null,
       "sampling gap cannot be described as an immediate damage burst");
danger.Reset();
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1400, Health = 700 }) is null &&
       danger.Observe(safeBaseline with { GameTimeSeconds = 1401, Health = 190 })
           is { Severity: OwnDangerSeverity.Critical },
       "fresh serious damage below 20 percent is announced precisely once");
Verify(danger.Observe(safeBaseline with { GameTimeSeconds = 1402, Health = 170 }) is null,
       "critical risk warning is not repeated merely because health stays low");


var episodes = new OwnDangerEpisodeTracker();
var episodeAnalyzer = new OwnDangerAnalyzer();
var episodeSample = own with { GameTimeSeconds = 1500, Health = 1000, MaxHealth = 1000 };
OwnDangerNotice? FeedEpisode(SelfStatsSnapshot sample) =>
    episodes.Observe(sample, episodeAnalyzer.Observe(sample));
Verify(FeedEpisode(episodeSample) is null && episodes.EpisodeCount == 0,
       "episode tracker cannot invent initial danger");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1501, Health = 650 }) is
       { Severity: OwnDangerSeverity.Elevated } &&
       episodes.EpisodeCount == 1 && episodes.IsActive,
       "first verified damage begins one episode");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1502, Health = 580 }) is null &&
       episodes.EpisodeCount == 1,
       "follow-up HP damage during same episode does not repeat speech");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1503, Health = 350 }) is
       { Severity: OwnDangerSeverity.High } &&
       episodes.EpisodeCount == 1 && episodes.CriticalEpisodeCount == 0,
       "a single danger episode escalates without incrementing episode count");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1504, Health = 140 }) is
       { Severity: OwnDangerSeverity.Critical } &&
       episodes.CriticalEpisodeCount == 1 && episodes.EpisodeCount == 1,
       "critical escalation counted once inside the same episode");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1505, Health = 130 }) is null &&
       episodes.CriticalEpisodeCount == 1,
       "remaining at critical HP does not repeat an unverified prediction");
Verify(episodes.GreatestEpisodeHealthLossPercent >= 86 &&
       episodes.LowestObservedEpisodeHealthPercent <= 13,
       "episode summary reports aggregate observed loss and lowest observed HP");
for (var steady = 1506; steady <= 1513; steady++)
    FeedEpisode(episodeSample with { GameTimeSeconds = steady, Health = 130 });
Verify(!episodes.IsActive && episodes.CompletedEpisodeCount == 1,
       "eight seconds without observed damage closes an episode but does not announce safety");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1514, Health = 80 }) is null &&
       episodes.EpisodeCount == 1,
       "small subsequent damage does not fabricate a new episode");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1515, Health = 0 }) is null &&
       !episodes.IsActive && episodes.CompletedEpisodeCount == 1,
       "own death does not create a fabricated follow-on danger notice");
episodes.ResetBaseline();
episodeAnalyzer.ResetBaseline();
Verify(episodes.EpisodeCount == 1 && episodes.CriticalEpisodeCount == 1,
       "respawn reset retains per-match episode summary");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1540, Health = 1000 }) is null &&
       FeedEpisode(episodeSample with { GameTimeSeconds = 1541, Health = 100 }) is
       { Severity: OwnDangerSeverity.Critical } &&
       episodes.EpisodeCount == 2 && episodes.CriticalEpisodeCount == 2,
       "new verified danger after respawn counts as a distinct episode");
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1, Health = 800 }) is null &&
       episodes.EpisodeCount == 0 && episodes.CriticalEpisodeCount == 0,
       "new match clock resets all danger episode statistics");
episodes.Reset();
episodeAnalyzer.Reset();
Verify(FeedEpisode(episodeSample with { GameTimeSeconds = 1700, Health = 800 }) is null &&
       FeedEpisode(episodeSample with { GameTimeSeconds = 1700, Health = 100 }) is null &&
       episodes.EpisodeCount == 0,
       "duplicate clock samples cannot introduce synthetic risk episodes");

Console.WriteLine($"ALL {count} CORE TESTS PASSED");
