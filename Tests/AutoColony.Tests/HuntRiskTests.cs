using System.Collections.Generic;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// What a hunting session costs in revenge.
    ///
    /// Run 161 is the whole argument: four Muffalo revenges in eight days, two colonists downed,
    /// from a hunt module that judged every animal on its own and found each of them a
    /// comfortable fight. The numbers here are the real def values, read out of
    /// Races_Animal_CowGroup.xml rather than remembered — muffalo manhunterOnDamageChance 0.1,
    /// baseHealthScale 1.75, against a rat's 0 and 0.29.
    /// </summary>
    public class HuntRiskTests
    {
        const float MuffaloPerWound = 0.1f;
        const float MuffaloHealth = 1.75f;
        const float RatHealth = 0.29f;
        const float MuffaloThreat = 100f;

        [Fact]
        public void APerWoundChanceIsNotAPerHuntChance()
        {
            // The fault, in one assertion. Ten percent per wound reads as a safe hunt; the
            // animal absorbs enough shooting to roll it several times over and comes back
            // roughly half of them.
            float wounds = HuntRisk.WoundsToFell(MuffaloHealth, 4f);
            float chance = HuntRisk.RevengeChance(MuffaloPerWound, wounds);

            Assert.True(chance > 0.4f, "a muffalo hunt should be near a coin flip, got " + chance);
            Assert.True(chance < 0.8f, "and not a certainty, got " + chance);
        }

        [Fact]
        public void ASmallAnimalIsNotMadeDangerousByTheSameArithmetic()
        {
            // The correction has to leave rats alone, or the colony stops eating. A rat rolls
            // no revenge at all because its per-wound chance is zero, however long it takes.
            Assert.Equal(0f, HuntRisk.RevengeChance(0f, HuntRisk.WoundsToFell(RatHealth, 4f)));
        }

        [Fact]
        public void AWoundedAnimalIsAlwaysWorthAtLeastOneRoll()
        {
            // Nothing dies without being hit once. A health scale small enough to round to zero
            // wounds must not make a warg free to hunt.
            Assert.True(HuntRisk.WoundsToFell(0.01f, 4f) >= 1f);
            Assert.Equal(1f, HuntRisk.RevengeChance(1f, HuntRisk.WoundsToFell(0.01f, 4f)));
        }

        [Fact]
        public void FiveHuntsAtHalfEachIsNotAHalfChanceEvening()
        {
            // Day 3 designated five muffalo in a single pass. Each was judged on its own as 514
            // strength against 100 threat and taken. This is what the pass was actually buying.
            var chances = new List<float>();
            float each = HuntRisk.RevengeChance(MuffaloPerWound,
                                                HuntRisk.WoundsToFell(MuffaloHealth, 4f));
            for (int i = 0; i < 5; i++) chances.Add(each);

            Assert.True(HuntRisk.AnyRevenge(chances) > 0.9f,
                "five muffalo is near-certain retaliation, got " + HuntRisk.AnyRevenge(chances));
        }

        [Fact]
        public void HarmlessPreyNeverAccumulatesRisk()
        {
            // A dozen rats and a dozen deer buy nothing, so the colony must stay free to hunt
            // them in any number. This is what removes the need for a clause exempting them.
            var chances = new List<float>();
            var threats = new List<float>();
            for (int i = 0; i < 12; i++) { chances.Add(0f); threats.Add(0f); }

            Assert.Equal(0f, HuntRisk.AnyRevenge(chances));
            Assert.Equal(0f, HuntRisk.ExpectedRetaliation(chances, threats));
        }

        [Fact]
        public void TheSessionIsWhatTheBarShouldJudge()
        {
            // The change, end to end, at run 161's own numbers: strength 514, desperation 0.38.
            // One muffalo is a hunt the colony can afford and should take. Enough of them is not,
            // and today nothing anywhere notices the difference.
            const float strength = 514f;
            const float desperation = 0.38f;

            float each = HuntRisk.RevengeChance(MuffaloPerWound,
                                                HuntRisk.WoundsToFell(MuffaloHealth, 4f));
            var chances = new List<float>();
            var threats = new List<float>();

            int affordable = 0;
            for (int i = 0; i < 30; i++)
            {
                chances.Add(each);
                threats.Add(MuffaloThreat);
                // The same bar ShouldHuntDangerous applies, reached directly: CombatAssessment
                // touches Pawn and cannot be compiled here, and for prey that fights back
                // RequiredRatio uses DangerousPreyFloor and ignores the desperate ratio.
                if (!HuntPolicy.WorthHunting(
                        strength, HuntRisk.ExpectedRetaliation(chances, threats),
                        true, desperation, 0.5f))
                    break;
                affordable++;
            }

            Assert.True(affordable >= 1, "one muffalo must still be huntable at 514 strength");
            Assert.True(affordable < 30,
                "the colony must stop somewhere; it took every animal it was offered before");
        }

        [Fact]
        public void APredatorCostsExactlyWhatItCostBefore()
        {
            // A predator does not flee a hunter, so its revenge is a certainty rather than a
            // chance, and a certainty times its threat is its threat. That keeps every predator
            // hunt judged at the bar it is judged at today — the correction moves the herbivores
            // this was wrong about and leaves alone the ones it was right about.
            var chances = new List<float> { 1f };
            var threats = new List<float> { 240f };

            Assert.Equal(240f, HuntRisk.ExpectedRetaliation(chances, threats), 3);
        }

        [Fact]
        public void ACautiousGenomeStopsSoonerThanABoldOne()
        {
            // How much shooting a hunt takes is not knowable at designation time — weapon,
            // range, skill and cover all decide it — so the scale is a gene rather than a
            // number this file picked. A colony that believes hunts are long sees more risk.
            float cautious = HuntRisk.RevengeChance(
                MuffaloPerWound, HuntRisk.WoundsToFell(MuffaloHealth, 10f));
            float bold = HuntRisk.RevengeChance(
                MuffaloPerWound, HuntRisk.WoundsToFell(MuffaloHealth, 1f));

            Assert.True(cautious > bold);
        }

        [Fact]
        public void ThreeMaulingsRaiseTheBarOnBuyingAFourth()
        {
            // The loop the standing brief asks for: a bad outcome has to cost the director
            // something it can measure. ThreatMemory has been measuring it all along — run 161
            // went 1.50x, 1.69x, 1.90x across three manhunter fights — and the hunt module that
            // was buying those fights could not hear it.
            //
            // The prior and the lesson are the same shape, so a fresh colony behaves exactly as
            // it does today and one that has been hurt asks for more margin.
            float naive = HuntPolicy.RequiredRatio(true, 0.38f, 0.5f,
                                                   HuntPolicy.DangerousPreyFloor);
            float taught = HuntPolicy.RequiredRatio(true, 0.38f, 0.5f, 1.90f);

            Assert.True(taught > naive,
                "a colony that has been mauled three times must want more margin, not the same");
        }

        [Fact]
        public void AColonyThatHasMetNothingUsesThePriorUnchanged()
        {
            // ForceFor falls back to the gene until the kind has been met, so this must be the
            // identical number the old constant produced or every fresh colony changes
            // behaviour for no reason.
            Assert.Equal(HuntPolicy.RequiredRatio(true, 0.38f, 0.5f),
                         HuntPolicy.RequiredRatio(true, 0.38f, 0.5f,
                                                  HuntPolicy.DangerousPreyFloor), 5);
        }
    }
}
