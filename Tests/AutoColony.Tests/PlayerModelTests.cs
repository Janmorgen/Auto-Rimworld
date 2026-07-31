using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The player model turns observations into a starting strategy. Getting the fitting
    /// wrong would be invisible in-game — the search would simply start somewhere unhelpful
    /// and nobody could tell that from ordinary slow progress.
    /// </summary>
    public class PlayerModelTests
    {
        static PlayerModel Observed(int samples)
        {
            var model = new PlayerModel();
            for (int i = 0; i < samples; i++)
            {
                model.samples++;
                model.foodDaysSum += 14f;
                model.woodSum += 750f;
                model.steelSum += 900f;
                model.componentsSum += 20f;
                model.textilesSum += 250f;
                model.medicinePerColonistSum += 4f;
                model.growCellsPerColonistSum += 90f;
                model.stockCellsPerColonistSum += 55f;
                model.medCareSum += 2f;
                model.selfTendSum += 1f;
            }
            return model;
        }

        [Fact]
        public void IsNotTrustedUntilEnoughHasBeenSeen()
        {
            Assert.False(new PlayerModel().IsUsable);
            Assert.False(Observed(PlayerModel.MinSamples - 1).IsUsable);
            Assert.True(Observed(PlayerModel.MinSamples).IsUsable);
        }

        [Fact]
        public void StockTargetsMatchWhatThePlayerHeld()
        {
            var genome = Observed(200).ToGenome();

            Assert.Equal(14f, genome.Get(Genes.FoodDaysPerColonist), 2);
            Assert.Equal(750f, genome.Get(Genes.WoodTarget), 1);
            Assert.Equal(900f, genome.Get(Genes.SteelTarget), 1);
            Assert.Equal(20f, genome.Get(Genes.ComponentsTarget), 2);
            Assert.Equal(250f, genome.Get(Genes.TextilesTarget), 1);
            Assert.Equal(90f, genome.Get(Genes.GrowingCellsPerColonist), 2);
            Assert.Equal(55f, genome.Get(Genes.StockpileCellsPerColonist), 2);
        }

        [Fact]
        public void WorkWeightsPreserveRelativeEmphasis()
        {
            // The gene controls relative emphasis, so what must survive fitting is the ratio
            // between work types, not the raw observation values.
            var model = Observed(200);
            for (int i = 0; i < 200; i++)
            {
                model.AddWorkEmphasis("Hauling", 0.8f);
                model.AddWorkEmphasis("Cooking", 0.4f);
                model.AddWorkEmphasis("Art", 0.0f);
            }

            var genome = model.ToGenome();
            float hauling = genome.Get(Genes.WorkKey("Hauling"));
            float cooking = genome.Get(Genes.WorkKey("Cooking"));
            float art = genome.Get(Genes.WorkKey("Art"));

            Assert.True(hauling > cooking, "hauling was pushed harder and should weigh more");
            Assert.True(cooking > art, "art was never assigned and should weigh least");
            Assert.Equal(2f, hauling / cooking, 1);
        }

        [Fact]
        public void WorkWeightsStayInsideGeneBounds()
        {
            var model = Observed(200);
            for (int i = 0; i < 200; i++)
            {
                // One work type dominating everything would push the ratio far past the cap.
                model.AddWorkEmphasis("Hauling", 1f);
                model.AddWorkEmphasis("Art", 0.0001f);
            }

            var genome = model.ToGenome();
            var spec = Genes.Spec(Genes.WorkKey("Hauling"));

            Assert.InRange(genome.Get(Genes.WorkKey("Hauling")), spec.Min, spec.Max);
            Assert.InRange(genome.Get(Genes.WorkKey("Art")), spec.Min, spec.Max);
        }

        [Fact]
        public void RoomGeometryIsReadFromWhatWasBuilt()
        {
            var model = Observed(200);
            for (int i = 0; i < 10; i++)
            {
                model.bedsPerRoomSum += 1f;   // the player builds private bedrooms
                model.roomSizeSum += 6f;
                model.roomSamples++;
            }

            var genome = model.ToGenome();
            Assert.Equal(1f, genome.Get(Genes.BaseBedsPerRoom), 2);
            Assert.Equal(6f, genome.Get(Genes.BaseRoomSize), 2);
        }

        [Fact]
        public void UnobservedRoomsLeaveTheDefaultsAlone()
        {
            var genome = Observed(200).ToGenome();
            Assert.Equal(Genes.Spec(Genes.BaseBedsPerRoom).Default, genome.Get(Genes.BaseBedsPerRoom), 3);
        }

        [Fact]
        public void FavouriteChoicesAreTheMostFrequentOnes()
        {
            var model = new PlayerModel();
            for (int i = 0; i < 10; i++) model.CountCrop("Plant_Rice");
            for (int i = 0; i < 3; i++) model.CountCrop("Plant_Corn");
            model.CountResearch("Electricity");

            Assert.Equal("Plant_Rice", model.FavouriteCrop);
            Assert.Equal("Electricity", model.FavouriteResearch);
        }

        [Fact]
        public void AnEmptyModelYieldsPlainDefaults()
        {
            var genome = new PlayerModel().ToGenome();
            Assert.Equal(0f, genome.DistanceTo(StrategyGenome.Default()), 6);
        }

        [Fact]
        public void FittedValuesAreClampedIntoLegalRanges()
        {
            // A colony sitting on an absurd stockpile must not produce an out-of-range gene.
            var model = new PlayerModel();
            for (int i = 0; i < 200; i++)
            {
                model.samples++;
                model.foodDaysSum += 5000f;
                model.woodSum += 1000000f;
            }

            var genome = model.ToGenome();
            Assert.Equal(Genes.Spec(Genes.FoodDaysPerColonist).Max, genome.Get(Genes.FoodDaysPerColonist), 2);
            Assert.Equal(Genes.Spec(Genes.WoodTarget).Max, genome.Get(Genes.WoodTarget), 1);
        }
    }
}
