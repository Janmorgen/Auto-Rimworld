using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Whether a colonist walking away from a fight carries the one who cannot walk.
    ///
    /// Three colonists lost to the same few seconds. Run 164 is the clearest: Simon went down at
    /// day 5 10h, the colony judged the fight lost and withdrew two able colonists past him to
    /// the refuge, and he was kidnapped at 11h. The rescue could not run because rescuing is work
    /// and every colonist was drafted.
    /// </summary>
    public class RetreatCargoTests
    {
        [Fact]
        public void AWithdrawalAlwaysCarries()
        {
            // The case that has cost three colonists, and the one where the answer is easy: the
            // line is already being given up, so the fighter spent on the carry gives up nothing
            // that was in use.
            Assert.True(RetreatCargo.WorthCarrying(withdrawing: true, carrierValue: 200f,
                                                   strengthSpare: 0f));
            Assert.True(RetreatCargo.WorthCarrying(true, 200f, -500f));
        }

        [Fact]
        public void ALineThatCannotSpareAnybodyKeepsEverybody()
        {
            // Standing and fighting is the harder case and gets the honest answer: a fighter
            // pulled out of a fight the colony still expects to win is a real loss.
            Assert.False(RetreatCargo.WorthCarrying(withdrawing: false, carrierValue: 100f,
                                                    strengthSpare: 0f));
            Assert.False(RetreatCargo.WorthCarrying(false, 100f, 40f));
        }

        [Fact]
        public void ALineWithSlackCanAffordTheCarry()
        {
            Assert.True(RetreatCargo.WorthCarrying(false, carrierValue: 100f, strengthSpare: 150f));
        }

        [Fact]
        public void TheFastestCarrierWins()
        {
            // A casualty on the ground with hostiles nearby is on a clock measured in seconds,
            // so distance dominates. Same colonist value, different distances.
            float near = RetreatCargo.CarrierFitness(5f, 100f, 4.6f);
            float far = RetreatCargo.CarrierFitness(60f, 100f, 4.6f);

            Assert.True(near > far);
        }

        [Fact]
        public void ACrippledCarrierLosesToAHealthyOneAtTheSameDistance()
        {
            // The same reason MedicChoice exists: RimWorld charges a missing part to MoveSpeed,
            // so a hurt carrier reads as the slow walker they are.
            float healthy = RetreatCargo.CarrierFitness(30f, 100f, 4.6f);
            float crippled = RetreatCargo.CarrierFitness(30f, 100f, 1.2f);

            Assert.True(healthy > crippled);
        }

        [Fact]
        public void SomebodyWhoCannotMoveIsNeverChosen()
        {
            Assert.Equal(0f, RetreatCargo.CarrierFitness(30f, 100f, 0f));
            Assert.Equal(0f, RetreatCargo.CarrierFitness(-1f, 100f, 4.6f));
        }

        [Fact]
        public void ValueIsATiebreakAndNotTheTerm()
        {
            // The colony would rather send its best fighter and keep the casualty than keep the
            // fighter in a line it has already abandoned — so a nearer, more valuable colonist
            // still beats a distant cheap one.
            float bestFighterClose = RetreatCargo.CarrierFitness(5f, 400f, 4.6f);
            float weakestFarAway = RetreatCargo.CarrierFitness(50f, 20f, 4.6f);

            Assert.True(bestFighterClose > weakestFarAway);
        }
    }
}
