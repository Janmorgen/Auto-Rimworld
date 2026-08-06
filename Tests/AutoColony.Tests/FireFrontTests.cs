using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The numbers here are the ones a colony actually burned to: 4 fires at a hundred cells,
    /// left alone because they were distant, then 13, 43, 52, 68, 85, 123, 135, 192, 227, 255
    /// over the following twenty-seven in-game hours.
    ///
    /// What these pin down is that the first sample is the one that matters, and that the answer
    /// stops being "go" once the front is past what the people present could put out.
    /// </summary>
    public class FireFrontTests
    {
        [Fact]
        public void TheFourFiresThatBurnedTheMapAreMet()
        {
            // 4 fires up from 1, a hundred cells out, one colonist. Outside any sane response
            // radius and coming anyway — the whole point of the test.
            Assert.True(FireFront.IsClosing(fires: 4, previousFires: 1, nearest: 100f,
                                            previousNearest: 104f, handsFree: 1, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void AFrontThatIsNotGrowingIsNotComing()
        {
            Assert.False(FireFront.IsClosing(fires: 4, previousFires: 4, nearest: 100f,
                                             previousNearest: 100f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void AShrinkingFrontIsLeftAlone()
        {
            Assert.False(FireFront.IsClosing(fires: 2, previousFires: 9, nearest: 100f,
                                             previousNearest: 100f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void AGrowingFrontMovingAwayWasNeverComing()
        {
            // The case the old distance rule was right about, and the reason growth alone is
            // not the test: a wildfire spreading away from the colony is not the colony's.
            Assert.False(FireFront.IsClosing(fires: 8, previousFires: 4, nearest: 120f,
                                             previousNearest: 100f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void NoiseInTheNearestSampleDoesNotCountAsRetreat()
        {
            Assert.True(FireFront.IsClosing(fires: 8, previousFires: 4, nearest: 100.5f,
                                            previousNearest: 100f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void AFrontBeyondWhatThePeoplePresentCanBeatIsNotAnswered()
        {
            // 43 fires and one colonist. Sending them loses the colonist and not the fire.
            Assert.False(FireFront.IsClosing(fires: 43, previousFires: 13, nearest: 80f,
                                             previousNearest: 100f, handsFree: 1, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void MorePeopleMakeALargerFrontWorthMeeting()
        {
            Assert.True(FireFront.IsClosing(fires: 30, previousFires: 13, nearest: 80f,
                                            previousNearest: 100f, handsFree: 5, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void TheFirstSampleHasNothingToCompareAgainst()
        {
            Assert.False(FireFront.IsClosing(fires: 4, previousFires: -1, nearest: 100f,
                                             previousNearest: -1f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void AColonyWithNobodyOnTheirFeetFightsNothing()
        {
            Assert.False(FireFront.IsClosing(fires: 2, previousFires: 1, nearest: 10f,
                                             previousNearest: 20f, handsFree: 0, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void NoFireIsNoFront()
        {
            Assert.False(FireFront.IsClosing(fires: 0, previousFires: 4, nearest: -1f,
                                             previousNearest: 100f, handsFree: 3, firesPerColonist: FireFront.DefaultFightableFiresPerColonist));
        }

        [Fact]
        public void SomebodyOnTheFloorIsInDangerWellBeforeTheFireArrives()
        {
            // The whole point of the reframing: act at a distance where there is still no fire
            // between the carrier and the casualty, so the question of pathing through flame
            // never has to be answered.
            Assert.True(FireFront.Threatens(1f));
            Assert.True(FireFront.Threatens(FireFront.DangerRadius));
            Assert.False(FireFront.Threatens(FireFront.DangerRadius + 0.1f));
        }

        [Fact]
        public void NoFireAtAllThreatensNobody()
        {
            // -1 is "nothing burning", which must not read as distance zero.
            Assert.False(FireFront.Threatens(-1f));
        }

        [Fact]
        public void TheDangerRadiusAllowsTimeToCarrySomebody()
        {
            // A carry is slow and fire spreads while it happens, so this has to be generous
            // enough to be acting on a fire that has not arrived rather than one that has.
            Assert.True(FireFront.DangerRadius >= 8f);
        }

        [Fact]
        public void FightableScalesWithThePeopleAvailable()
        {
            Assert.True(FireFront.Fightable(6, 1, 6));
            Assert.False(FireFront.Fightable(7, 1, 6));
            Assert.True(FireFront.Fightable(18, 3, 6));
            Assert.False(FireFront.Fightable(19, 3, 6));
        }

        [Fact]
        public void ADraftedColonistIsNotAHandFreeToFightAFire()
        {
            // Found by the connection map rather than by a colony: asking which modules write
            // world.labourAvailable turned up DefenseModule and WorkPriorityModule, and
            // DefenseModule was reading ableColonists — which includes the drafted, because
            // CombatAssessment.RankFighters needs exactly those — where it meant hands free.
            //
            // It drafts against a raid and then asks whether the colony can still fight a fire.
            // A raid with incendiaries makes both true at once from one event, so this is the
            // common case rather than the corner one. Four colonists all drafted is zero hands,
            // and zero hands cannot beat one fire however able those four are.
            Assert.False(FireFront.Fightable(fires: 1, handsFree: 0, firesPerColonist: 6));
            Assert.True(FireFront.Fightable(fires: 1, handsFree: 1, firesPerColonist: 6));
        }

        [Fact]
        public void TheColonyMayDisagreeWithSixFiresAColonist()
        {
            // The rate was a const the colony could not argue with, and where the front outruns
            // the people depends on wind, fuel and how far they have to walk — all of which vary
            // by map. A cautious genome commits to less; a bold one to more.
            Assert.False(FireFront.Fightable(fires: 10, handsFree: 2, firesPerColonist: 4));
            Assert.True(FireFront.Fightable(fires: 10, handsFree: 2, firesPerColonist: 9));
        }

        [Fact]
        public void ARateOfZeroWouldMakeEveryFrontHopelessAndIsRefused()
        {
            // A genome is free to be cautious, not to be incoherent: a colony that believes one
            // colonist can beat no fires never fights any fire, including the one in its kitchen.
            Assert.True(FireFront.Fightable(fires: 1, handsFree: 1, firesPerColonist: 0));
        }
    }
}
