using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// What the colony learns from a fight it survived.
    ///
    /// Run 168 day 23: a mad cougar met at 1.55x against a required 1.50x, won without anybody
    /// going down, costing 0.52 health across three sent and leaving two colonists bleeding out.
    /// Health went 1.00 to 0.83, and the memory learned nothing from any of it.
    /// </summary>
    public class ThreatMemoryTests
    {
        [Fact]
        public void AFightThatLeavesSomebodyBleedingIsNotACheapFight()
        {
            // The change, stated directly. Same damage, same absence of casualties, and the only
            // difference is whether anyone walked away from it still losing blood.
            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.03f, 0, 0);
            float afterClean = ThreatMemory.For(ThreatKind.Manhunter).force;

            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.03f, 0, 2);
            float afterBleeding = ThreatMemory.For(ThreatKind.Manhunter).force;

            Assert.True(afterBleeding > afterClean,
                "bleeding must not read as cheap; clean=" + afterClean + " bleeding=" + afterBleeding);
        }

        [Fact]
        public void WalkingAwayUntouchedStillLowersTheBar()
        {
            // The behaviour being preserved: a genuinely cheap fight should still teach the
            // colony it can bring fewer hands. Breaking that would make the colony permanently
            // over-cautious, which costs it every hand it holds back from work.
            ThreatMemory.Clear();
            float before = ThreatMemory.For(ThreatKind.Manhunter).force;
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.03f, 0, 0);

            Assert.True(ThreatMemory.For(ThreatKind.Manhunter).force < before);
        }

        [Fact]
        public void TheFourArgumentFormStillMeansNobodyWasBleeding()
        {
            // Existing callers must not change behaviour by being left alone.
            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.03f, 0);
            float four = ThreatMemory.For(ThreatKind.Manhunter).force;

            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.03f, 0, 0);

            Assert.Equal(four, ThreatMemory.For(ThreatKind.Manhunter).force, 5);
        }

        [Fact]
        public void OneOnTheFloorOutweighsOneStillStanding()
        {
            // Somebody down is worse than somebody hurt, one for one. Not "worse than any amount
            // of bleeding" — three colonists all losing blood genuinely is a worse afternoon than
            // one knocked out, and the arithmetic should say so.
            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.6f, 1, 0);
            float down = ThreatMemory.For(ThreatKind.Manhunter).force;

            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.6f, 0, 1);

            Assert.True(down > ThreatMemory.For(ThreatKind.Manhunter).force);
        }

        [Fact]
        public void EnoughBleedingOutweighsASingleCasualty()
        {
            // The other side of the same statement, pinned so nobody "fixes" it later: a fight
            // that leaves the whole party bleeding is not milder than one that drops a single
            // colonist, and the weight the colony puts on that is its own to evolve.
            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.6f, 1, 0);
            float oneDown = ThreatMemory.For(ThreatKind.Manhunter).force;

            ThreatMemory.Clear();
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.6f, 0, 3);

            Assert.True(ThreatMemory.For(ThreatKind.Manhunter).force > oneDown);
        }

        [Fact]
        public void TheRun168CougarNoLongerTeachesNothing()
        {
            // 0.52 health across 3 sent is 0.17 each, which falls between WalkedAway and
            // HurtBadly — so the fight moved the number by exactly zero while two colonists
            // bled. It must now register as something.
            ThreatMemory.Clear();
            float before = ThreatMemory.For(ThreatKind.Manhunter).force;
            ThreatMemory.RecordOutcome(ThreatKind.Manhunter, 3, 0.52f, 0, 2);

            Assert.NotEqual(before, ThreatMemory.For(ThreatKind.Manhunter).force);
        }
    }
}
