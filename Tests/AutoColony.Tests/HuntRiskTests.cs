using System.Collections.Generic;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// What a hunting session costs in revenge.
    ///
    /// Run 161 is the whole argument: three Muffalo revenges in eight days, two colonists downed,
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

namespace AutoColony.Tests
{
    /// <summary>
    /// The boomalope, which killed a colony on day 0 of run 174 and Stephanie on day 10 of
    /// run 196, both times after the hunt priced its bite and not its death.
    /// </summary>
    public class BlastHazardTests
    {
        /// <summary>Ordinary prey carries no blast, so nothing here can ever refuse a deer.</summary>
        [Fact]
        public void AnAnimalThatDoesNotExplodeCostsNothing()
        {
            Assert.Equal(0f, HuntRisk.BlastHazard(0f, false, 3f));
            Assert.Equal(0f, HuntRisk.BlastHazard(-1f, true, 3f));
        }

        /// <summary>Area, not radius — a blast covers a disc and the damage scales with it.</summary>
        [Fact]
        public void HazardScalesWithAreaNotRadius()
        {
            float small = HuntRisk.BlastHazard(2f, false, 1f);
            float big = HuntRisk.BlastHazard(4f, false, 1f);
            Assert.Equal(4f, small);
            Assert.Equal(16f, big);
            Assert.Equal(4f, big / small);   // double the radius, four times the hazard
        }

        /// <summary>
        /// Fire is worse than concussion, and by a factor the strategy chooses. Nineteen fires
        /// against one able colonist is a different event from one crater.
        /// </summary>
        [Fact]
        public void AnIncendiaryBlastCostsMoreThanAPlainOne()
        {
            float plain = HuntRisk.BlastHazard(3f, false, 3f);
            float fire = HuntRisk.BlastHazard(3f, true, 3f);
            Assert.True(fire > plain);
            Assert.Equal(plain * 3f, fire);
        }

        /// <summary>A weight below one would make fire cheaper than concussion. It cannot.</summary>
        [Fact]
        public void TheIncendiaryWeightNeverDiscountsFire()
        {
            float plain = HuntRisk.BlastHazard(3f, false, 1f);
            Assert.Equal(plain, HuntRisk.BlastHazard(3f, true, 0f));
            Assert.Equal(plain, HuntRisk.BlastHazard(3f, true, -5f));
        }
    }
}

namespace AutoColony.Tests
{
    /// <summary>
    /// Blackrose, who met a megasloth alone at full health while the colony's summed strength
    /// said the fight was comfortable. Run 197, day 16.
    /// </summary>
    public class SurvivesContactTests
    {
        /// <summary>Harmless prey is never refused by this — it has nothing to meet.</summary>
        [Fact]
        public void NothingToMeetIsAlwaysSurvivable()
        {
            Assert.True(HuntRisk.SurvivesContact(0f, 0f, 1.3f));
            Assert.True(HuntRisk.SurvivesContact(50f, 0f, 3f));
        }

        /// <summary>
        /// The observed case. Best single colonist about 177, megasloth measured 221 — losing at
        /// any margin, however many guns are pointed at its back.
        /// </summary>
        [Fact]
        public void TheMegaslothBeatsTheBestColonistTheColonyHas()
        {
            Assert.False(HuntRisk.SurvivesContact(177f, 221f, 1.3f));
            Assert.False(HuntRisk.SurvivesContact(177f, 221f, 1.0f));
        }

        /// <summary>Clearly ahead is fine, and must be, or the colony hunts nothing.</summary>
        [Fact]
        public void SomebodyClearlyStrongerMayTakeIt()
        {
            Assert.True(HuntRisk.SurvivesContact(300f, 221f, 1.3f));
            Assert.True(HuntRisk.SurvivesContact(100f, 50f, 1.3f));
        }

        /// <summary>
        /// An even fight is refused at the default margin and allowed at 1.0 — which is the
        /// whole point of the margin being a gene rather than a constant.
        /// </summary>
        [Fact]
        public void TheMarginIsWhatSeparatesAnEvenFightFromASafeOne()
        {
            Assert.False(HuntRisk.SurvivesContact(221f, 221f, 1.3f));
            Assert.True(HuntRisk.SurvivesContact(221f, 221f, 1.0f));
        }

        /// <summary>A margin below one cannot make the colony bolder than an even fight.</summary>
        [Fact]
        public void TheMarginNeverGoesBelowEven()
        {
            Assert.False(HuntRisk.SurvivesContact(100f, 221f, 0f));
            Assert.False(HuntRisk.SurvivesContact(100f, 221f, -5f));
        }

        /// <summary>Nobody left standing meets nothing safely.</summary>
        [Fact]
        public void NobodyToSendSurvivesNothing()
        {
            Assert.False(HuntRisk.SurvivesContact(0f, 10f, 1.3f));
        }
    }
}
