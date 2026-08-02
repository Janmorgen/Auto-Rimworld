using AutoColony.Rooms;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// Defects were ranked by kind alone, so a dark room scored the same whether it was the only
    /// kitchen or a spare bedroom nobody sleeps in.
    /// </summary>
    public class RoomImportanceTests
    {
        const float Essential = 0.8f;
        const float Occupancy = 0.6f;

        static RoomFacts Room(bool essential, bool unique, int users, int colonists)
        {
            var f = new RoomFacts();
            f.essential = essential;
            f.unique = unique;
            f.users = users;
            f.colonists = colonists;
            return f;
        }

        [Fact]
        public void ABusyRoomOutranksAnEmptyOne()
        {
            float busy = RoomImportance.Of(Room(false, false, 3, 3), Essential, Occupancy);
            float empty = RoomImportance.Of(Room(false, false, 0, 3), Essential, Occupancy);
            Assert.True(busy > empty);
        }

        [Fact]
        public void ARoomTheColonyDependsOnOutranksOneItDoesNot()
        {
            float kitchen = RoomImportance.Of(Room(true, false, 0, 3), Essential, Occupancy);
            float spare = RoomImportance.Of(Room(false, false, 0, 3), Essential, Occupancy);
            Assert.True(kitchen > spare);
        }

        [Fact]
        public void TheOnlyKitchenOutranksOneOfTwo()
        {
            float only = RoomImportance.Of(Room(true, true, 0, 3), Essential, Occupancy);
            float oneOfSeveral = RoomImportance.Of(Room(true, false, 0, 3), Essential, Occupancy);
            Assert.True(only > oneOfSeveral);
        }

        [Fact]
        public void BeingTheOnlySpareBedroomIsNotSpecial()
        {
            // Uniqueness only counts for a room that does something; an empty bedroom being the
            // only empty bedroom is not a reason to fix its lighting first.
            float unique = RoomImportance.Of(Room(false, true, 0, 3), Essential, Occupancy);
            float notUnique = RoomImportance.Of(Room(false, false, 0, 3), Essential, Occupancy);
            Assert.Equal(notUnique, unique, 3);
        }

        [Fact]
        public void OccupancyIsAShareNotACount()
        {
            // Two of two matters more than two of ten.
            float small = RoomImportance.Of(Room(false, false, 2, 2), Essential, Occupancy);
            float large = RoomImportance.Of(Room(false, false, 2, 10), Essential, Occupancy);
            Assert.True(small > large);
        }

        [Fact]
        public void MoreUsersThanColonistsDoesNotRunAway()
        {
            float capped = RoomImportance.Of(Room(false, false, 20, 3), Essential, Occupancy);
            float full = RoomImportance.Of(Room(false, false, 3, 3), Essential, Occupancy);
            Assert.Equal(full, capped, 3);
        }

        [Fact]
        public void AStrategyThatIgnoresRoomsFallsBackToRankingOnTheFaultAlone()
        {
            // Both weights at zero: every room is worth the same, which is the old behaviour and
            // must remain reachable by the search rather than being designed out.
            float kitchen = RoomImportance.Of(Room(true, true, 3, 3), 0f, 0f);
            float spare = RoomImportance.Of(Room(false, false, 0, 3), 0f, 0f);
            Assert.Equal(spare, kitchen, 3);
        }

        [Fact]
        public void NoColonistsDoesNotDivideByZero()
        {
            float importance = RoomImportance.Of(Room(false, false, 0, 0), Essential, Occupancy);
            Assert.True(importance > 0f);
        }
    }
}
