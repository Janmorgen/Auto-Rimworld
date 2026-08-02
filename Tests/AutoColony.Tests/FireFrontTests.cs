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
                                            previousNearest: 104f, ableColonists: 1));
        }

        [Fact]
        public void AFrontThatIsNotGrowingIsNotComing()
        {
            Assert.False(FireFront.IsClosing(fires: 4, previousFires: 4, nearest: 100f,
                                             previousNearest: 100f, ableColonists: 3));
        }

        [Fact]
        public void AShrinkingFrontIsLeftAlone()
        {
            Assert.False(FireFront.IsClosing(fires: 2, previousFires: 9, nearest: 100f,
                                             previousNearest: 100f, ableColonists: 3));
        }

        [Fact]
        public void AGrowingFrontMovingAwayWasNeverComing()
        {
            // The case the old distance rule was right about, and the reason growth alone is
            // not the test: a wildfire spreading away from the colony is not the colony's.
            Assert.False(FireFront.IsClosing(fires: 8, previousFires: 4, nearest: 120f,
                                             previousNearest: 100f, ableColonists: 3));
        }

        [Fact]
        public void NoiseInTheNearestSampleDoesNotCountAsRetreat()
        {
            Assert.True(FireFront.IsClosing(fires: 8, previousFires: 4, nearest: 100.5f,
                                            previousNearest: 100f, ableColonists: 3));
        }

        [Fact]
        public void AFrontBeyondWhatThePeoplePresentCanBeatIsNotAnswered()
        {
            // 43 fires and one colonist. Sending them loses the colonist and not the fire.
            Assert.False(FireFront.IsClosing(fires: 43, previousFires: 13, nearest: 80f,
                                             previousNearest: 100f, ableColonists: 1));
        }

        [Fact]
        public void MorePeopleMakeALargerFrontWorthMeeting()
        {
            Assert.True(FireFront.IsClosing(fires: 30, previousFires: 13, nearest: 80f,
                                            previousNearest: 100f, ableColonists: 5));
        }

        [Fact]
        public void TheFirstSampleHasNothingToCompareAgainst()
        {
            Assert.False(FireFront.IsClosing(fires: 4, previousFires: -1, nearest: 100f,
                                             previousNearest: -1f, ableColonists: 3));
        }

        [Fact]
        public void AColonyWithNobodyOnTheirFeetFightsNothing()
        {
            Assert.False(FireFront.IsClosing(fires: 2, previousFires: 1, nearest: 10f,
                                             previousNearest: 20f, ableColonists: 0));
        }

        [Fact]
        public void NoFireIsNoFront()
        {
            Assert.False(FireFront.IsClosing(fires: 0, previousFires: 4, nearest: -1f,
                                             previousNearest: 100f, ableColonists: 3));
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
            Assert.True(FireFront.Fightable(6, 1));
            Assert.False(FireFront.Fightable(7, 1));
            Assert.True(FireFront.Fightable(18, 3));
            Assert.False(FireFront.Fightable(19, 3));
        }
    }
}
