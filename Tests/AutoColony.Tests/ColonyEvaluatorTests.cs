using System.Collections.Generic;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Property tests on the fitness function.
    ///
    /// The evaluator defines what "playing well" means, so a sign error here would send the
    /// whole search confidently in the wrong direction while every other component looked
    /// healthy. These assert the directions that must never invert.
    /// </summary>
    public class ColonyEvaluatorTests
    {
        static ColonyMetrics Baseline()
        {
            var m = ColonyMetrics.Neutral();
            m.colonists = 5;
            m.colonistBeds = 5;
            m.avgMood = 0.7f;
            m.avgHealth = 0.95f;
            m.daysOfFood = 10f;
            m.wealthTotal = 20000f;
            m.researchFinished = 10;
            m.poweredTurrets = 1;
            return m;
        }

        static EpochStart StartFrom(ColonyMetrics m)
        {
            var s = EpochStart.From(m);
            s.wealthTotal = 18000f;
            s.researchFinished = 9;
            return s;
        }

        /// <summary>
        /// A colony leaving wounds untended is not a healthy one.
        ///
        /// avgHealth is SummaryHealthPercent, which counts damaged body parts and is blind to
        /// hediffs — so colonists have died of Infection (extreme) while the term reported them
        /// in near-perfect health. The score could not tell a colony that tended its wounded
        /// from one that watched them die of an infection.
        /// </summary>
        [Fact]
        public void LeavingWoundsUntendedScoresBelowTendingThem()
        {
            var end = Baseline();

            var tended = new EpochAccumulator();
            var neglected = new EpochAccumulator();

            var well = Baseline();
            var untended = Baseline();
            untended.colonistsUntended = 1;    // identical body-part health, nobody tending

            for (int i = 0; i < 40; i++)
            {
                tended.Observe(well);
                neglected.Observe(untended);
            }

            Assert.True(neglected.UntendedFraction > 0.9f, "somebody was untended throughout");
            Assert.Equal(tended.AvgHealth, neglected.AvgHealth, 3);

            List<ScoreTerm> a, b;
            float tendedScore = ColonyEvaluator.Evaluate(StartFrom(end), end, tended, out a);
            float neglectedScore = ColonyEvaluator.Evaluate(StartFrom(end), end, neglected, out b);

            Assert.True(neglectedScore < tendedScore,
                "a colony that left its wounded untended must not score as well as one that tended them");
        }

        /// <summary>
        /// A full larder is not a fed colony.
        ///
        /// Seven colonies have died with Food security at or near 1.00 — run 93 on day 20 with a
        /// colonist down for 30% of the epoch and thirty-three days of food in store. The term
        /// measured the larder, so the search was repeatedly told that the way they died was a
        /// success on the one axis that most decides whether a colony lives.
        /// </summary>
        [Fact]
        public void StarvingBesideAFullLarderScoresBelowAFedColony()
        {
            var end = Baseline();
            end.daysOfFood = 30f;

            var fed = new EpochAccumulator();
            var starving = new EpochAccumulator();

            var sample = Baseline();
            sample.daysOfFood = 30f;

            var hungry = Baseline();
            hungry.daysOfFood = 30f;          // the larder is full the whole time
            hungry.colonistsStarving = 1;     // and somebody is not eating from it

            for (int i = 0; i < 40; i++)
            {
                fed.Observe(sample);
                starving.Observe(hungry);
            }

            Assert.True(fed.FoodSecurity > 0.9f, "a stocked colony should read secure");
            Assert.True(starving.FoodSecurity > 0.9f, "the larder was equally full in both");
            Assert.True(starving.StarvingFraction > 0.9f, "somebody was starving throughout");

            List<ScoreTerm> a, b;
            float fedScore = ColonyEvaluator.Evaluate(StartFrom(end), end, fed, out a);
            float starvingScore = ColonyEvaluator.Evaluate(StartFrom(end), end, starving, out b);

            Assert.True(starvingScore < fedScore,
                "a colony that starved beside its own food must not score as well as one that ate");
        }

        /// <summary>Runs one epoch's worth of identical observations, then scores it.</summary>
        static float Score(ColonyMetrics end, int deaths = 0, int samples = 40)
        {
            var start = StartFrom(Baseline());
            var acc = new EpochAccumulator();

            var first = end;
            first.cumulativeDeaths = 0;
            acc.ResetFor(first);

            for (int i = 0; i < samples; i++)
            {
                var obs = end;
                obs.cumulativeDeaths = deaths;
                acc.Observe(obs);
            }

            List<ScoreTerm> breakdown;
            return ColonyEvaluator.Evaluate(start, end, acc, out breakdown);
        }

        [Fact]
        public void ScoreAlwaysLiesInUnitInterval()
        {
            var rng = new AcRandom(808);
            for (int i = 0; i < 500; i++)
            {
                var m = Baseline();
                m.colonists = rng.Range(0, 20);
                m.colonistBeds = rng.Range(0, 25);
                m.avgMood = rng.Value;
                m.avgHealth = rng.Value;
                m.daysOfFood = rng.Value * 40f;
                m.wealthTotal = rng.Value * 200000f;
                m.poweredTurrets = rng.Range(0, 15);
                m.researchFinished = rng.Range(0, 40);

                float score = Score(m, rng.Range(0, 6));
                Assert.InRange(score, 0f, 1f);
            }
        }

        [Fact]
        public void MoreDeathsNeverRaisesTheScore()
        {
            float previous = float.MaxValue;
            for (int deaths = 0; deaths <= 5; deaths++)
            {
                float score = Score(Baseline(), deaths);
                Assert.True(score <= previous + 1e-6f,
                    "score rose when deaths went up to " + deaths);
                previous = score;
            }
        }

        [Fact]
        public void MoreFoodNeverLowersTheScore()
        {
            float previous = float.MinValue;
            for (float days = 0f; days <= 20f; days += 2f)
            {
                var m = Baseline();
                m.daysOfFood = days;
                float score = Score(m);
                Assert.True(score >= previous - 1e-6f,
                    "score fell when food reserves rose to " + days + " days");
                previous = score;
            }
        }

        [Fact]
        public void BetterMoodNeverLowersTheScore()
        {
            float previous = float.MinValue;
            for (float mood = 0f; mood <= 1f; mood += 0.1f)
            {
                var m = Baseline();
                m.avgMood = mood;
                float score = Score(m);
                Assert.True(score >= previous - 1e-6f, "score fell as mood rose to " + mood);
                previous = score;
            }
        }

        [Fact]
        public void AWipedColonyScoresZero()
        {
            var m = Baseline();
            m.colonists = 0;
            Assert.Equal(0f, Score(m, 5), 5);
        }

        [Fact]
        public void FoodSecurityUsesTheWorstReserveNotTheFinalOne()
        {
            // A colony that starved for most of an epoch and recovered on the last day must
            // not score as though it were comfortable throughout.
            var start = StartFrom(Baseline());

            var end = Baseline();
            end.daysOfFood = 15f;

            var starved = new EpochAccumulator();
            starved.ResetFor(end);
            var lean = end;
            lean.daysOfFood = 0.5f;
            for (int i = 0; i < 39; i++) starved.Observe(lean);
            starved.Observe(end);

            var comfortable = new EpochAccumulator();
            comfortable.ResetFor(end);
            for (int i = 0; i < 40; i++) comfortable.Observe(end);

            List<ScoreTerm> a, b;
            float starvedScore = ColonyEvaluator.Evaluate(start, end, starved, out a);
            float comfortableScore = ColonyEvaluator.Evaluate(start, end, comfortable, out b);

            Assert.True(starvedScore < comfortableScore,
                "identical endpoints must still score differently when the epoch went badly");
        }

        /// <summary>Fills an accumulator with `lean` samples at `low` days and the rest comfortable.</summary>
        static EpochAccumulator Epoch(ColonyMetrics comfortable, int lean, float low, int total = 40)
        {
            var acc = new EpochAccumulator();
            acc.ResetFor(comfortable);

            // One comfortable sample first, so the larder is measurable from the start and the
            // lean stretch is recorded as a real dip rather than the opening hours.
            acc.Observe(comfortable);

            var thin = comfortable;
            thin.daysOfFood = low;
            for (int i = 0; i < lean; i++) acc.Observe(thin);
            for (int i = acc.samples; i < total; i++) acc.Observe(comfortable);
            return acc;
        }

        [Fact]
        public void OneEmptyHourIsNotAStarvingEpoch()
        {
            // The case the term was changed for. Both of these scored 0.00 on the old measure,
            // because both touched zero at some point and the score was the single worst reading.
            var end = Baseline();

            var dipped = Epoch(end, lean: 1, low: 0.1f);
            var starving = Epoch(end, lean: 35, low: 0.1f);

            Assert.True(dipped.FoodSecurity > 0.9f,
                "an epoch that dipped once was secure almost throughout");
            Assert.True(starving.FoodSecurity < 0.2f,
                "an epoch spent short of food was not");
        }

        [Fact]
        public void TimeSpentShortIsWhatSeparatesTheTwo()
        {
            var start = StartFrom(Baseline());
            var end = Baseline();

            List<ScoreTerm> a, b;
            float dipped = ColonyEvaluator.Evaluate(start, end, Epoch(end, 1, 0.1f), out a);
            float starving = ColonyEvaluator.Evaluate(start, end, Epoch(end, 35, 0.1f), out b);

            Assert.True(dipped > starving,
                "a colony that ran out briefly must outscore one that was short all epoch");
        }

        [Fact]
        public void FoodSecurityIsTheFractionOfTimeOutOfDanger()
        {
            var end = Baseline();
            var acc = Epoch(end, lean: 10, low: 0.1f, total: 40);

            Assert.Equal(40, acc.foodSamples);
            Assert.Equal(30, acc.foodSecureSamples);
            Assert.Equal(0.75f, acc.FoodSecurity, 3);
        }

        [Fact]
        public void FoodJustAboveTheDangerLineCountsAsSecure()
        {
            // The line is the supply lead time: below it nothing decided now arrives in time.
            var end = Baseline();

            var acc = new EpochAccumulator();
            acc.ResetFor(end);
            var atLine = end;
            atLine.daysOfFood = EpochAccumulator.FoodDangerDays;
            for (int i = 0; i < 10; i++) acc.Observe(atLine);

            Assert.Equal(1f, acc.FoodSecurity, 3);
        }

        [Fact]
        public void AColonyThatNeverStockpiledAnythingScoresZero()
        {
            // Unchanged from the old measure, and deliberately: no measurable sample is no
            // evidence of security, not perfect security.
            var end = Baseline();
            end.daysOfFood = 0f;

            var acc = new EpochAccumulator();
            acc.ResetFor(end);
            for (int i = 0; i < 40; i++) acc.Observe(end);

            Assert.Equal(0, acc.foodSamples);
            Assert.Equal(0f, acc.FoodSecurity, 3);
        }

        [Fact]
        public void BreakdownContributionsSumToTheScore()
        {
            var start = StartFrom(Baseline());
            var end = Baseline();
            var acc = new EpochAccumulator();
            acc.ResetFor(end);
            for (int i = 0; i < 20; i++) acc.Observe(end);

            List<ScoreTerm> breakdown;
            float score = ColonyEvaluator.Evaluate(start, end, acc, out breakdown);

            float sum = 0f;
            foreach (var term in breakdown) sum += term.Contribution;

            Assert.Equal(score, sum, 4);
        }

        [Fact]
        public void WealthGrowthIsScaleFree()
        {
            // The same proportional growth should score the same for a poor colony and a rich
            // one, otherwise the score inflates as the colony matures and epochs stop comparing.
            var poorStart = new EpochStart { colonists = 5, wealthTotal = 10000f, researchFinished = 9 };
            var richStart = new EpochStart { colonists = 5, wealthTotal = 400000f, researchFinished = 9 };

            var poorEnd = Baseline();
            poorEnd.wealthTotal = 12000f;
            var richEnd = Baseline();
            richEnd.wealthTotal = 480000f;

            var acc = new EpochAccumulator();
            acc.ResetFor(poorEnd);
            for (int i = 0; i < 20; i++) acc.Observe(poorEnd);

            List<ScoreTerm> a, b;
            ColonyEvaluator.Evaluate(poorStart, poorEnd, acc, out a);
            ColonyEvaluator.Evaluate(richStart, richEnd, acc, out b);

            float poorGrowth = a.Find(t => t.name == "Growth").raw;
            float richGrowth = b.Find(t => t.name == "Growth").raw;

            Assert.Equal(poorGrowth, richGrowth, 3);
        }

        // ------------------------------------------------------- room quality

        /// <summary>Scores an epoch in which every sample saw this fraction of the base up to standard.</summary>
        static float ScoreWithRooms(int judged, int upToStandard, int samples = 40)
        {
            var end = Baseline();
            end.roomsJudged = judged;
            end.roomsUpToStandard = upToStandard;

            var start = StartFrom(Baseline());
            var acc = new EpochAccumulator();
            acc.ResetFor(end);
            for (int i = 0; i < samples; i++) acc.Observe(end);

            List<ScoreTerm> breakdown;
            return ColonyEvaluator.Evaluate(start, end, acc, out breakdown);
        }

        [Fact]
        public void ABaseUpToStandardOutscoresOneThatIsNot()
        {
            // The whole point of the term: two colonies identical in every outcome figure —
            // same deaths, food, mood, wealth, beds — differing only in whether the rooms they
            // built were worth building. Before this the search could not tell them apart, so
            // the room width and height genes had no gradient and simply drifted.
            Assert.True(ScoreWithRooms(6, 6) > ScoreWithRooms(6, 0));
        }

        [Fact]
        public void MoreOfTheBaseUpToStandardNeverLowersTheScore()
        {
            float worse = ScoreWithRooms(4, 1);
            float better = ScoreWithRooms(4, 3);
            Assert.True(better >= worse);
        }

        [Fact]
        public void AColonyWithNothingRateableScoresNeutralRatherThanZero()
        {
            // No enclosed rooms is no evidence either way. Such a colony is already losing
            // Infrastructure and Growth for it, and a third penalty would let one fact decide
            // three terms.
            var acc = new EpochAccumulator();
            Assert.Equal(0.5f, acc.RoomQuality, 4);

            // And it must beat a colony whose rooms were rated and found wanting.
            Assert.True(ScoreWithRooms(0, 0) > ScoreWithRooms(6, 0));
        }

        [Fact]
        public void RoomQualityIsTheTimeAveragedFractionUpToStandard()
        {
            var acc = new EpochAccumulator();
            var m = Baseline();

            m.roomsJudged = 4; m.roomsUpToStandard = 4;
            acc.Observe(m);
            m.roomsUpToStandard = 0;
            acc.Observe(m);

            // Half the epoch fully up to standard, half of it not at all.
            Assert.Equal(0.5f, acc.RoomQuality, 4);
        }

        [Fact]
        public void SamplesWithNothingBuiltYetAreNotCountedAgainstTheColony()
        {
            // Every colony's opening hours have no enclosed rooms. Counting those as a base
            // failing its standards would score the first day against everyone equally.
            var acc = new EpochAccumulator();
            var m = Baseline();

            m.roomsJudged = 0; m.roomsUpToStandard = 0;
            for (int i = 0; i < 20; i++) acc.Observe(m);
            Assert.Equal(0, acc.roomQualitySamples);

            m.roomsJudged = 2; m.roomsUpToStandard = 2;
            acc.Observe(m);
            Assert.Equal(1, acc.roomQualitySamples);
            Assert.Equal(1f, acc.RoomQuality, 4);
        }

        [Fact]
        public void TheWeightsStillSumToOne()
        {
            // Room quality was taken proportionally out of the others rather than added on
            // top. If a future term is added by increasing the total instead, the score leaves
            // [0,1] and every archived score becomes incomparable in a way nothing announces.
            List<ScoreTerm> breakdown;
            ColonyEvaluator.Evaluate(StartFrom(Baseline()), Baseline(), FullAcc(), out breakdown);

            float total = 0f;
            for (int i = 0; i < breakdown.Count; i++) total += breakdown[i].weight;
            Assert.Equal(1f, total, 4);
        }

        static EpochAccumulator FullAcc()
        {
            var acc = new EpochAccumulator();
            var m = Baseline();
            acc.ResetFor(m);
            for (int i = 0; i < 40; i++) acc.Observe(m);
            return acc;
        }
    }
}
