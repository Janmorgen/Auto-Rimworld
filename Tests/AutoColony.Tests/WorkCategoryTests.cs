using System.Collections.Generic;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The dimensionality cut. One gene per work type was roughly twenty of a fifty-eight gene
    /// genome, against a search that gets a handful of epochs and measured two candidates from
    /// an identical world at 0.561 and 0.565.
    /// </summary>
    public class WorkCategoryTests
    {
        static readonly string[] VanillaWorkTypes =
        {
            "Firefighter", "Patient", "Doctor", "PatientBedRest", "Flicker", "Warden", "Handling",
            "Cooking", "Hunting", "Construction", "Growing", "Mining", "PlantCutting", "Smithing",
            "Tailoring", "Art", "Crafting", "Hauling", "Cleaning", "Research"
        };

        [Fact]
        public void TwentyWorkTypesCollapseToAHandfulOfGenes()
        {
            var keys = new HashSet<string>();
            for (int i = 0; i < VanillaWorkTypes.Length; i++)
                keys.Add(Genes.WorkKey(VanillaWorkTypes[i]));

            Assert.True(keys.Count <= 8, "expected a handful of categories, got " + keys.Count);
            Assert.True(keys.Count < VanillaWorkTypes.Length);
        }

        [Fact]
        public void WorkOfTheSamePurposeSharesAWeight()
        {
            Assert.Equal(Genes.WorkKey("Growing"), Genes.WorkKey("Cooking"));
            Assert.Equal(Genes.WorkKey("Construction"), Genes.WorkKey("Mining"));
            Assert.Equal(Genes.WorkKey("Doctor"), Genes.WorkKey("Firefighter"));
        }

        [Fact]
        public void WorkOfDifferentPurposeDoesNot()
        {
            Assert.NotEqual(Genes.WorkKey("Growing"), Genes.WorkKey("Construction"));
            Assert.NotEqual(Genes.WorkKey("Doctor"), Genes.WorkKey("Art"));
        }

        [Fact]
        public void ModdedWorkTypesAreCoveredWithoutCostingAGeneEach()
        {
            // They share "other" rather than each adding a dimension the search cannot afford.
            Assert.Equal(Genes.WorkKey("SomeModdedWork"), Genes.WorkKey("AnotherModdedWork"));
        }

        [Fact]
        public void RegisteringEveryWorkTypeAddsOnlyTheCategories()
        {
            int before = Genes.All.Count;
            for (int i = 0; i < VanillaWorkTypes.Length; i++)
                Genes.RegisterWorkType(VanillaWorkTypes[i], VanillaWorkTypes[i]);

            int added = Genes.All.Count - before;
            Assert.True(added <= 8, "registering 20 work types added " + added + " genes");
        }

        [Fact]
        public void EveryCategoryGeneIsRegisteredAndUsable()
        {
            for (int i = 0; i < VanillaWorkTypes.Length; i++)
                Genes.RegisterWorkType(VanillaWorkTypes[i], VanillaWorkTypes[i]);

            var genome = StrategyGenome.Default();
            for (int i = 0; i < VanillaWorkTypes.Length; i++)
            {
                var key = Genes.WorkKey(VanillaWorkTypes[i]);
                Assert.NotNull(Genes.Spec(key));
                Assert.True(genome.Get(key) > 0f);
            }
        }
    }
}
