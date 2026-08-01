using System.Collections.Generic;
using AutoColony;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Scoring how the epoch was *run*, not only how it came out.
    ///
    /// The case this exists for is a real one: a colony was wiped on day twelve after eleven days
    /// carrying a standing -10 nobody could answer and lurching between emergencies, and every
    /// outcome figure looked survivable right up until it was not. Two colonies with identical
    /// end states are not equally well run, and the score could not previously say so.
    /// </summary>
    public class ConductScoringTests
    {
        static ColonyMetrics Healthy()
        {
            var m = ColonyMetrics.Neutral();
            m.colonists = 3;
            m.avgMood = 0.7f;
            m.avgHealth = 1f;
            m.daysOfFood = 12f;
            m.wealthTotal = 10000f;
            m.colonistBeds = 3;
            m.day = 10;
            return m;
        }

        /// <summary>An epoch run calmly, with nothing outstanding the director could not fix.</summary>
        static EpochAccumulator Calm(ColonyMetrics m, int samples = 100)
        {
            var acc = new EpochAccumulator();
            acc.ResetFor(m);
            for (int i = 0; i < samples; i++) acc.Observe(m);
            return acc;
        }

        static float Score(EpochAccumulator acc, ColonyMetrics end)
        {
            List<ScoreTerm> breakdown;
            return ColonyEvaluator.Evaluate(EpochStart.From(end), end, acc, out breakdown);
        }

        static ScoreTerm TermNamed(EpochAccumulator acc, ColonyMetrics end, string name)
        {
            List<ScoreTerm> breakdown;
            ColonyEvaluator.Evaluate(EpochStart.From(end), end, acc, out breakdown);
            for (int i = 0; i < breakdown.Count; i++)
                if (breakdown[i].name == name) return breakdown[i];

            Assert.Fail("no term named " + name);
            return default(ScoreTerm);
        }

        // ------------------------------------------------------------ an epoch nobody watched

        [Fact]
        public void AnEpochWithNoSamplesIsNotWorthScoring()
        {
            // The overnight failure: a snapshot taken with an already-elapsed epoch made every
            // trial close on its first tick, re-scoring an accumulator with nothing in it. Fifty
            // eight of sixty two scores came out identical and the search learned nothing.
            var acc = new EpochAccumulator();
            acc.ResetFor(Healthy());
            Assert.False(acc.Scorable);
        }

        [Fact]
        public void AFewSamplesAreStillNotEnough()
        {
            var end = Healthy();
            var acc = Calm(end, EpochAccumulator.MinSamplesToScore - 1);
            Assert.False(acc.Scorable);
        }

        [Fact]
        public void AnObservedEpochIsScorable()
        {
            var end = Healthy();
            Assert.True(Calm(end, EpochAccumulator.MinSamplesToScore).Scorable);
            Assert.True(Calm(end, 500).Scorable);
        }

        [Fact]
        public void AnEmptyAccumulatorScoresIdenticallyEveryTime()
        {
            // Why the guard exists rather than trusting the number: with no samples every term
            // falls back on a default, so two completely different colonies score the same.
            var poor = Healthy();
            poor.wealthTotal = 500f; poor.avgMood = 0.05f; poor.daysOfFood = 0f;

            var rich = Healthy();
            rich.wealthTotal = 90000f; rich.avgMood = 0.95f; rich.daysOfFood = 30f;

            var a = new EpochAccumulator(); a.ResetFor(poor);
            var b = new EpochAccumulator(); b.ResetFor(rich);

            // Same mood/health/emergency inputs from defaults — the terms that differ are only
            // the end-state ones, so the accumulator contributes nothing to tell them apart.
            Assert.Equal(a.AvgMood, b.AvgMood, 4);
            Assert.Equal(a.AvgHealth, b.AvgHealth, 4);
            Assert.Equal(a.EmergencyFraction, b.EmergencyFraction, 4);
            Assert.False(a.Scorable);
            Assert.False(b.Scorable);
        }

        // ------------------------------------------------------------ the weights still add up

        [Fact]
        public void WeightsSumToOne()
        {
            var end = Healthy();
            List<ScoreTerm> breakdown;
            ColonyEvaluator.Evaluate(EpochStart.From(end), end, Calm(end), out breakdown);

            float total = 0f;
            for (int i = 0; i < breakdown.Count; i++) total += breakdown[i].weight;
            Assert.InRange(total, 0.999f, 1.001f);
        }

        [Fact]
        public void ScoreStaysWithinRangeWhateverTheConduct()
        {
            var end = Healthy();
            var awful = Calm(end, 0);
            awful.ResetFor(end);
            for (int i = 0; i < 50; i++)
            {
                var crisis = end;
                crisis.inEmergency = true;
                awful.Observe(crisis);
                awful.NoteUnmetComplaints(500f, "Everything", 500f);
            }
            Assert.InRange(Score(awful, end), 0f, 1f);
        }

        // ------------------------------------------------------------ emergencies

        [Fact]
        public void AnEpochSpentInCrisisScoresBelowOneSpentBuilding()
        {
            var end = Healthy();

            var calm = Calm(end);
            var crisis = new EpochAccumulator();
            crisis.ResetFor(end);
            var emergency = end;
            emergency.inEmergency = true;
            for (int i = 0; i < 100; i++) crisis.Observe(emergency);

            // Identical end states. The only difference is how the fortnight was spent.
            Assert.True(Score(calm, end) > Score(crisis, end));
        }

        [Fact]
        public void EmergencyFractionIsTheProportionOfSamples()
        {
            var end = Healthy();
            var acc = new EpochAccumulator();
            acc.ResetFor(end);

            var emergency = end;
            emergency.inEmergency = true;
            for (int i = 0; i < 25; i++) acc.Observe(emergency);
            for (int i = 0; i < 75; i++) acc.Observe(end);

            Assert.InRange(acc.EmergencyFraction, 0.24f, 0.26f);
        }

        // ------------------------------------------------------------ unanswerable misery

        [Fact]
        public void MiseryWithNoRemedyLowersTheScore()
        {
            var end = Healthy();

            var content = Calm(end);
            var miserable = Calm(end);
            for (int i = 0; i < 10; i++) miserable.NoteUnmetComplaints(35f, "ColonistLeftUnburied", 10f);

            Assert.True(Score(content, end) > Score(miserable, end));
        }

        [Fact]
        public void TheWorstComplaintIsRemembered()
        {
            var end = Healthy();
            var acc = Calm(end);

            acc.NoteUnmetComplaints(8f, "NeedComfort", 3f);
            acc.NoteUnmetComplaints(14f, "ColonistLeftUnburied", 10f);
            acc.NoteUnmetComplaints(6f, "AteWithoutTable", 3f);

            // Not the largest total, nor the most recent — the one that hurt most at once, which
            // is the one worth teaching the director to fix.
            Assert.Equal("ColonistLeftUnburied", acc.worstComplaint);
            Assert.Equal(10f, acc.worstComplaintMood);
        }

        [Fact]
        public void ComplaintsAreAveragedPerSurveyNotSummedForever()
        {
            var end = Healthy();

            var brief = Calm(end);
            brief.NoteUnmetComplaints(20f, "X", 5f);

            var long_ = Calm(end);
            for (int i = 0; i < 20; i++) long_.NoteUnmetComplaints(20f, "X", 5f);

            // The same standing misery for longer must not read as twenty times worse, or a long
            // epoch would score worse than a short one for behaving identically.
            Assert.Equal(brief.AvgUnmetComplaints, long_.AvgUnmetComplaints, 3);
        }

        [Fact]
        public void NothingUnansweredIsNotAPenalty()
        {
            var end = Healthy();
            var acc = Calm(end);
            Assert.Equal(0f, acc.AvgUnmetComplaints);
            Assert.Equal(1f, TermNamed(acc, end, "Conduct").raw, 3);
        }

        // ------------------------------------------------------------ the day-12 shape

        [Fact]
        public void TheColonyThatDiedNextWouldHaveScoredWorseWhileStillAlive()
        {
            // Both survive the epoch with the same numbers. One of them spent it in a permanent
            // emergency carrying misery it could not answer — and that is the one that was about
            // to come apart. Before Conduct these scored identically.
            var end = Healthy();

            var steady = Calm(end);

            var doomed = new EpochAccumulator();
            doomed.ResetFor(end);
            var struggling = end;
            struggling.inEmergency = true;
            for (int i = 0; i < 100; i++)
            {
                doomed.Observe(struggling);
                if (i % 10 == 0) doomed.NoteUnmetComplaints(38f, "ColonistLeftUnburied", 10f);
            }

            float gap = Score(steady, end) - Score(doomed, end);
            Assert.True(gap > 0.05f, "conduct should separate them meaningfully, gap was " + gap);
        }

        [Fact]
        public void WasteIsRecordedButNotScored()
        {
            var end = Healthy();

            var tidy = Calm(end);
            var thrashing = Calm(end);
            thrashing.NoteWaste(50);

            // Deliberately not a penalty: a raid levelling a room is not the director's fault,
            // and a destitute colony reclaiming its own walls is doing the right thing.
            Assert.Equal(50, thrashing.wastedActions);
            Assert.Equal(Score(tidy, end), Score(thrashing, end), 4);
        }
    }
}
