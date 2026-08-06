using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Which colonist is worth holding back to tend the wounded.
    ///
    /// Run 162 is the case. A rhinoceros revenge put Pansy on the floor bleeding and took Poole's
    /// right leg in the same fight. Poole had Medicine 7, was correctly identified as the best
    /// doctor, was held back to tend — and Pansy died of blood loss six hours later with
    /// twenty-two medicine in store.
    /// </summary>
    public class MedicChoiceTests
    {
        const int Hour = 2500;

        [Fact]
        public void ASurgeonWhoArrivesAfterTheDeathIsWorthNothing()
        {
            // The run 162 death. Skill 7 against skill 0, and the deadline decides.
            float crippled = MedicChoice.Usefulness(skill: 7, ticksToReach: 7 * Hour,
                                                    ticksUntilDeath: 6 * Hour);
            Assert.Equal(0f, crippled);
        }

        [Fact]
        public void AnUntrainedColonistStandingThereBeatsASurgeonWhoCannotArrive()
        {
            // Not a claim about medicine. A colonist with no training who is already beside the
            // patient can stop the bleeding; a better one who is still walking cannot.
            float here = MedicChoice.Usefulness(0, ticksToReach: 0, ticksUntilDeath: 6 * Hour);
            float walking = MedicChoice.Usefulness(7, ticksToReach: 7 * Hour, 6 * Hour);

            Assert.True(here > walking);
        }

        [Fact]
        public void SkillStillDecidesBetweenMedicsWhoCanBothGetThere()
        {
            // What the old ranking had right, and this must not lose: when the deadline is not
            // binding, the better doctor is the better answer.
            float good = MedicChoice.Usefulness(7, ticksToReach: Hour, ticksUntilDeath: 12 * Hour);
            float poor = MedicChoice.Usefulness(2, ticksToReach: Hour, ticksUntilDeath: 12 * Hour);

            Assert.True(good > poor);
        }

        [Fact]
        public void ArrivingWithNothingToSpareIsWorthLessThanArrivingEarly()
        {
            // Pansy died with medicine in store. A doctor who reaches the patient in the last
            // minutes has no time to fetch any, so the margin is part of the usefulness rather
            // than a line the answer either clears or does not.
            float early = MedicChoice.Usefulness(5, ticksToReach: Hour, ticksUntilDeath: 10 * Hour);
            float late = MedicChoice.Usefulness(5, ticksToReach: 9 * Hour, ticksUntilDeath: 10 * Hour);

            Assert.True(early > late);
        }

        [Fact]
        public void NobodyBleedingMeansTheOldQuestionIsTheRightOne()
        {
            // Reserving a medic against future casualties is a different judgement with no clock
            // on it, and skill alone is the honest answer there.
            Assert.True(MedicChoice.Usefulness(7, 9 * Hour, 0) >
                        MedicChoice.Usefulness(2, 0, 0));
        }

        [Fact]
        public void NoRouteIsNotASlowRoute()
        {
            Assert.Equal(0f, MedicChoice.Usefulness(9, MedicChoice.Unreachable, 6 * Hour));
        }

        [Fact]
        public void ALostLegShowsUpAsTheSlowWalkItIs()
        {
            // RimWorld charges a missing part to MoveSpeed, so this needs no notion of a leg.
            // Thirty cells at a healthy 4.6 against a crippled 1.2.
            int healthy = MedicChoice.TicksToCross(30f, 4.6f);
            int crippled = MedicChoice.TicksToCross(30f, 1.2f);

            Assert.True(crippled > healthy * 3);
        }

        [Fact]
        public void AColonistWhoCannotMoveAtAllIsUnreachableRatherThanInstant()
        {
            Assert.Equal(MedicChoice.Unreachable, MedicChoice.TicksToCross(30f, 0f));
            Assert.Equal(0, MedicChoice.TicksToCross(0f, 4.6f));
        }
    }
}
