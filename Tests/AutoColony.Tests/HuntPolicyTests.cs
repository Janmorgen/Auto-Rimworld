using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The escalation these describe killed a colony inside six hours, and every one of the
    /// cases below is taken from the chronicle of that run rather than invented.
    ///
    /// Day 1, three passes in the same in-game hour: the first marked a Red fox, the second a
    /// Rat, the third found nothing new to mark and sent everyone after a Warg at 0.61x. An hour
    /// earlier the same reasoning had bought a Megasloth at 0.49x. Neither animal died; both
    /// went manhunter and followed the hunters home. Three colonists became one.
    /// </summary>
    public class HuntPolicyTests
    {
        const float Desperate = 0.95f;

        [Fact]
        public void StarvingColonyWithNothingInFlightFightsAnyway()
        {
            // The case the escalation exists for: no hunts out, nobody hurt, larder empty, and
            // the only animals on the map are ones the colony would rather not meet. Refusing
            // here is not survival, it is a slower way to lose.
            Assert.True(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 0, colonistsDowned: 0,
                desperation: Desperate, candidatesAvailable: 3));
        }

        [Fact]
        public void HuntsAlreadyOutMeanFoodIsComing()
        {
            // The pass that killed the colony. Nothing new could be designated for the sole
            // reason that everything worth designating already had been.
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 2, colonistsDowned: 0,
                desperation: Desperate, candidatesAvailable: 3));
        }

        [Fact]
        public void OneStandingHuntIsEnoughToWithholdIt()
        {
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 1, colonistsDowned: 0,
                desperation: Desperate, candidatesAvailable: 9));
        }

        [Fact]
        public void CasualtiesRuleOutStartingAFightTheColonyExpectsToLose()
        {
            // Strength is measured over the able, so a colony with someone down is already
            // weaker than the judgement assumed — and a wounded Megasloth comes home.
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 0, colonistsDowned: 1,
                desperation: Desperate, candidatesAvailable: 3));
        }

        [Fact]
        public void APassThatFoundSomethingSafeDoesNotEscalate()
        {
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 1, huntsAlreadyStanding: 0, colonistsDowned: 0,
                desperation: Desperate, candidatesAvailable: 3));
        }

        [Fact]
        public void NothingToEscalateOntoIsNotAnEscalation()
        {
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 0, colonistsDowned: 0,
                desperation: Desperate, candidatesAvailable: 0));
        }

        [Fact]
        public void AColonyThatIsMerelyHungryDoesNotTakeTheFight()
        {
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 0, colonistsDowned: 0,
                desperation: 0.5f, candidatesAvailable: 3));
        }

        [Fact]
        public void TheThresholdIsExclusiveSoDesperationMustExceedIt()
        {
            Assert.False(HuntPolicy.LastResortWarranted(
                designatedThisPass: 0, huntsAlreadyStanding: 0, colonistsDowned: 0,
                desperation: HuntPolicy.DesperateEnough, candidatesAvailable: 3));
        }

        // ------------------------------------------------------- prey that fights back

        /// <summary>The ordinary desperate floor, as CombatAssessment defines it.</summary>
        const float DesperateRatio = 0.5f;

        [Fact]
        public void HungerLowersTheBarForSafePreyAllTheWayDown()
        {
            // The existing behaviour, and the reason it is right: a starving colony refusing to
            // chase a hare is not being careful, it is starving on purpose.
            Assert.Equal(0.5f, HuntPolicy.RequiredRatio(false, 1f, DesperateRatio), 3);
        }

        [Fact]
        public void HungerCannotTalkTheColonyIntoAMarginalFightWithAPredator()
        {
            // Run 56 declined a cougar twice against a 1.5x bar, took it at 1.13x once hunger
            // had dropped the bar to 1.1x, and lost two colonists to the revenge.
            Assert.Equal(HuntPolicy.DangerousPreyFloor,
                         HuntPolicy.RequiredRatio(true, 1f, DesperateRatio), 3);
            Assert.False(HuntPolicy.WorthHunting(136f, 120f, true, 0.60f, DesperateRatio));
        }

        [Fact]
        public void TheSameAnimalIsStillTakenWhenTheColonyIsActuallyStrongEnough()
        {
            // The floor is 1.5x, not a ban. A colony at twice the cougar's strength eats.
            Assert.True(HuntPolicy.WorthHunting(240f, 120f, true, 0.60f, DesperateRatio));
        }

        [Fact]
        public void ADangerousAnimalIsNeverEasierToJustifyThanASafeOne()
        {
            for (float d = 0f; d <= 1f; d += 0.1f)
            {
                Assert.True(HuntPolicy.RequiredRatio(true, d, DesperateRatio) >=
                            HuntPolicy.RequiredRatio(false, d, DesperateRatio) - 0.0001f,
                            "desperation " + d);
            }
        }

        [Fact]
        public void AComfortableColonyIsEquallyPickyAboutBoth()
        {
            // At desperation 0 the floor never applies, so both start from the same place and
            // the change cannot make a well-fed colony hunt *more*.
            Assert.Equal(HuntPolicy.RequiredRatio(false, 0f, DesperateRatio),
                         HuntPolicy.RequiredRatio(true, 0f, DesperateRatio), 3);
        }

        [Fact]
        public void TheFloorDoesNotStrandAStarvingColony()
        {
            // LastResortWarranted is the door this floor would otherwise close: nothing safe
            // designated, nothing already coming, nobody down, and past the desperation line.
            Assert.True(HuntPolicy.LastResortWarranted(0, 0, 0, 0.95f, 3));
        }
    }
}
