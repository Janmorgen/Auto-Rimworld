using System.Collections.Generic;
using AutoColony;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Run 206's infestation, as arithmetic: ten hostiles close by the chord and sealed in rock
    /// by the path, which a radius called an attack four times an hour.
    /// </summary>
    public class ReachTests
    {
        /// <summary>
        /// No route is not a long walk. Every fault this file exists for comes from a sentinel
        /// that got treated as a measurement.
        /// </summary>
        [Fact]
        public void NoRouteIsNotADistance()
        {
            Assert.Equal(Reach.Unreachable, Reach.Hours(-1f, 4.6f));
            Assert.False(Reach.Imminent(Reach.Unreachable, 100f));
            Assert.False(Reach.Imminent(Reach.Unreachable, float.MaxValue));
        }

        /// <summary>A standing colonist with no speed cannot arrive, and must not read as here.</summary>
        [Fact]
        public void NoSpeedIsNoArrival()
        {
            Assert.Equal(Reach.Unreachable, Reach.Hours(50f, 0f));
            Assert.Equal(Reach.Unreachable, Reach.Hours(50f, -2f));
        }

        /// <summary>Distance becomes a clock, which is the point of the whole change.</summary>
        [Fact]
        public void DistanceBecomesHours()
        {
            // 4.6 cells/sec is an unencumbered pawn; 2500 ticks is an hour at 60 ticks a second.
            float hours = Reach.Hours(Reach.SecondsPerHour * 4.6f, 4.6f);
            Assert.Equal(1f, hours, 3);
        }

        /// <summary>
        /// The pruning rule, which is what makes pathfinding every hostile affordable. A path is
        /// never shorter than the chord, so a chord already longer than the best real path cannot
        /// win and neither can anything sorted behind it.
        /// </summary>
        [Fact]
        public void AChordLongerThanTheBestPathCannotWin()
        {
            Assert.False(Reach.CouldBeat(90f, 80f));
            Assert.True(Reach.CouldBeat(70f, 80f));
        }

        /// <summary>With nothing found yet every candidate is worth walking.</summary>
        [Fact]
        public void EverythingIsWorthTryingBeforeAnythingIsFound()
        {
            Assert.True(Reach.CouldBeat(500f, Reach.Unreachable));
            Assert.True(Reach.CouldBeat(0f, Reach.Unreachable));
        }

        /// <summary>
        /// The infestation. Ten hostiles, none with a route, and the answer has to be "no threat"
        /// rather than "the nearest is very far" — a colony cannot withdraw from the second.
        /// </summary>
        [Fact]
        public void NothingWithARouteIsNotAThreat()
        {
            var sealed_ = new List<float> { -1f, -1f, -1f, -1f, -1f };
            Assert.Equal(Reach.Unreachable, Reach.Nearest(sealed_));
            Assert.False(Reach.Imminent(Reach.Nearest(sealed_), 6f));
        }

        /// <summary>One that can get through decides it, however many cannot.</summary>
        [Fact]
        public void OneWithARouteSetsTheAnswer()
        {
            var mixed = new List<float> { -1f, 9f, -1f, 3f, -1f };
            Assert.Equal(3f, Reach.Nearest(mixed));
            Assert.True(Reach.Imminent(3f, 6f));
        }

        /// <summary>An empty map threatens nobody.</summary>
        [Fact]
        public void NoCandidatesIsNoThreat()
        {
            Assert.Equal(Reach.Unreachable, Reach.Nearest(new List<float>()));
            Assert.Equal(Reach.Unreachable, Reach.Nearest(null));
        }

        /// <summary>
        /// A breach that opens onto nowhere is a second pocket, not an exit, and has to lose to
        /// every real candidate rather than merely score badly.
        /// </summary>
        [Fact]
        public void ABreachOntoNowhereIsNotAnExit()
        {
            Assert.Equal(float.MaxValue, Reach.BreachCost(10f, -1f, 1f));
        }

        /// <summary>
        /// A thin wall onto a long detour loses to a thicker one that opens onto the base. That
        /// comparison is the whole reason this is measured rather than taken first-fit.
        /// </summary>
        [Fact]
        public void AShortWalkHomeBeatsACheapWall()
        {
            float thinWallLongWalk = Reach.BreachCost(10f, 120f, 1f);
            float thickWallShortWalk = Reach.BreachCost(40f, 5f, 1f);
            Assert.True(thickWallShortWalk < thinWallLongWalk);
        }

        /// <summary>With work priced at nothing, only the walk home matters.</summary>
        [Fact]
        public void AZeroWorkWeightComparesOnlyTheWalk()
        {
            Assert.Equal(120f, Reach.BreachCost(999f, 120f, 0f));
            Assert.Equal(5f, Reach.BreachCost(999f, 5f, 0f));
        }

        /// <summary>Nonsense work does not become a bonus.</summary>
        [Fact]
        public void NegativeWorkIsNotACredit()
        {
            Assert.Equal(20f, Reach.BreachCost(-50f, 20f, 1f));
        }
    }
}
