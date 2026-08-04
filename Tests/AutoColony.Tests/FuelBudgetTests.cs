using AutoColony.Furniture;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The rule exists to stop the director making a colony worse by helping.
    ///
    /// Run 110 spent its second week placing passive coolers and torch lamps while seven hoppers
    /// stood dry in a heatwave — every addition locally correct, and every one dividing the same
    /// woodpile and the same hauling hours further. These pin the two ends: a colony keeping up
    /// is never blocked, and a colony with none of something is never blocked either.
    /// </summary>
    public class FuelBudgetTests
    {
        [Fact]
        public void NothingDryIsNeverBehind()
        {
            Assert.False(FuelBudget.BehindOnFuel(0, 3));
            Assert.False(FuelBudget.BehindOnFuel(0, 1));
        }

        [Fact]
        public void FewerDryHoppersThanColonistsIsKeepingUp()
        {
            // Two of three colonists owe a trip; the third can take the next one.
            Assert.False(FuelBudget.BehindOnFuel(2, 3));
        }

        [Fact]
        public void OneDryHopperPerColonistIsBehind()
        {
            Assert.True(FuelBudget.BehindOnFuel(3, 3));
            Assert.True(FuelBudget.BehindOnFuel(7, 3));   // run 110, day 12
        }

        [Fact]
        public void TheThresholdMovesWithPopulationRatherThanBeingFixed()
        {
            // The same two dry hoppers: nothing to a colony of six, most of a day for a colony of two.
            Assert.False(FuelBudget.BehindOnFuel(2, 6));
            Assert.True(FuelBudget.BehindOnFuel(2, 2));
        }

        [Fact]
        public void FirstOfAKindIsAlwaysAllowed()
        {
            // No cooler at all in a heatwave: the answer is a cooler, however dry the stove is.
            Assert.True(FuelBudget.CanKeepAnotherFed(7, 3, 0));
        }

        [Fact]
        public void SecondOfAKindIsRefusedWhileBehind()
        {
            Assert.False(FuelBudget.CanKeepAnotherFed(7, 3, 1));
            Assert.False(FuelBudget.CanKeepAnotherFed(3, 3, 4));
        }

        [Fact]
        public void SecondOfAKindIsAllowedWhenKeepingUp()
        {
            Assert.True(FuelBudget.CanKeepAnotherFed(1, 3, 1));
            Assert.True(FuelBudget.CanKeepAnotherFed(0, 3, 9));
        }

        [Fact]
        public void ALoneColonistIsBehindAtOneDryHopper()
        {
            // Max(1, colonists) keeps a zero or one person colony from reading as infinitely capable.
            Assert.True(FuelBudget.BehindOnFuel(1, 1));
            Assert.True(FuelBudget.BehindOnFuel(1, 0));
        }
    }
}
