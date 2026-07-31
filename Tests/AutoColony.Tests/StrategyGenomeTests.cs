using System.Linq;
using System.Xml.Linq;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    public class StrategyGenomeTests
    {
        [Fact]
        public void MutationNeverEscapesGeneBounds()
        {
            // Genes drive things like room sizes and priority levels; an out-of-range value
            // would surface as a bad placement or an invalid work priority deep in the game.
            var rng = new AcRandom(1717);
            var genome = StrategyGenome.Default();

            for (int i = 0; i < 300; i++)
            {
                genome = genome.Mutate(rng, 0.9f, 1f);
                foreach (var spec in Genes.All)
                    Assert.InRange(genome.Get(spec.Key), spec.Min, spec.Max);
            }
        }

        [Fact]
        public void MutationAlwaysChangesSomething()
        {
            // A mutation rate of zero would otherwise burn an entire epoch re-testing a genome
            // identical to the incumbent.
            var rng = new AcRandom(31);
            var parent = StrategyGenome.Default();

            for (int i = 0; i < 20; i++)
            {
                var child = parent.Mutate(rng, 0.2f, 0f);
                Assert.True(child.DistanceTo(parent) > 0f, "mutation produced an identical genome");
            }
        }

        [Fact]
        public void GenerationIncrementsWithEachMutation()
        {
            var rng = new AcRandom(5);
            var genome = StrategyGenome.Default();
            Assert.Equal(0, genome.generation);

            genome = genome.Mutate(rng, 0.1f, 0.5f);
            Assert.Equal(1, genome.generation);

            genome = genome.Mutate(rng, 0.1f, 0.5f);
            Assert.Equal(2, genome.generation);
        }

        [Fact]
        public void CloneIsIndependentOfItsParent()
        {
            var original = StrategyGenome.Default();
            original.Set(Genes.WoodTarget, 500f);

            var copy = original.Clone();
            copy.Set(Genes.WoodTarget, 100f);

            Assert.Equal(500f, original.Get(Genes.WoodTarget), 3);
            Assert.Equal(100f, copy.Get(Genes.WoodTarget), 3);
        }

        [Fact]
        public void UnsetGenesFallBackToTheirDefault()
        {
            // Old saves must stay loadable when new genes are introduced.
            var genome = new StrategyGenome();
            var spec = Genes.Spec(Genes.FoodDaysPerColonist);

            Assert.Equal(spec.Default, genome.Get(Genes.FoodDaysPerColonist), 3);
        }

        [Fact]
        public void SetClampsToTheGeneRange()
        {
            var genome = new StrategyGenome();
            var spec = Genes.Spec(Genes.BaseRoomSize);

            genome.Set(Genes.BaseRoomSize, spec.Max + 1000f);
            Assert.Equal(spec.Max, genome.Get(Genes.BaseRoomSize), 3);

            genome.Set(Genes.BaseRoomSize, spec.Min - 1000f);
            Assert.Equal(spec.Min, genome.Get(Genes.BaseRoomSize), 3);
        }

        [Fact]
        public void XmlRoundTripPreservesEveryGene()
        {
            // This is the cross-save carry-over path; losing precision here would silently
            // degrade a strategy every time it passed through the archive.
            var rng = new AcRandom(77);
            var original = StrategyGenome.Default().Mutate(rng, 0.5f, 1f);

            var restored = StrategyGenome.FromXml(original.ToXml("genome"));

            Assert.Equal(0f, restored.DistanceTo(original), 6);
            Assert.Equal(original.generation, restored.generation);
            Assert.Equal(original.lineage, restored.lineage);
        }

        [Fact]
        public void AnUnmutatedGenomeStillSerialisesEveryGene()
        {
            // Regression: a genome nobody had Set() anything on archived as an empty element,
            // because only explicitly-set values were written. It reloaded correctly only
            // because the defaults happened to match — a trap the first time a default moves.
            var xml = StrategyGenome.Default().ToXml("genome");

            Assert.Equal(Genes.All.Count, xml.Elements("g").Count());
        }

        [Fact]
        public void ArchivedGenomeSurvivesADefaultChanging()
        {
            // Simulates a future version shipping a different default: the archived value must
            // win, because it is what the strategy was actually measured with.
            var original = StrategyGenome.Default();
            var spec = Genes.Spec(Genes.WoodTarget);
            original.Set(Genes.WoodTarget, spec.Default);

            var restored = StrategyGenome.FromXml(original.ToXml("genome"));

            Assert.Equal(spec.Default, restored.Get(Genes.WoodTarget), 3);
            Assert.Contains(restored.ToXml("g").Elements("g"),
                e => e.Attribute("k").Value == Genes.WoodTarget);
        }

        [Fact]
        public void GenesFromAnUnloadedModAreKeptRatherThanDropped()
        {
            // A work type belonging to a mod that is currently disabled has no spec, but its
            // tuning should survive so re-enabling the mod restores it.
            var genome = new StrategyGenome();
            genome.Set("work.w.SomeModdedWork", 2.5f);

            var restored = StrategyGenome.FromXml(genome.ToXml("genome"));

            Assert.Equal(2.5f, restored.Get("work.w.SomeModdedWork"), 3);
        }

        [Fact]
        public void FromXmlToleratesMissingAndMalformedInput()
        {
            Assert.NotNull(StrategyGenome.FromXml(null));

            var junk = new XElement("genome",
                new XElement("g", new XAttribute("k", "nope"), new XAttribute("v", "not-a-number")));
            var genome = StrategyGenome.FromXml(junk);

            Assert.Equal(Genes.Spec(Genes.WoodTarget).Default, genome.Get(Genes.WoodTarget), 3);
        }

        [Fact]
        public void DistanceIsZeroForIdenticalGenomesAndPositiveOtherwise()
        {
            var a = StrategyGenome.Default();
            Assert.Equal(0f, a.DistanceTo(a.Clone()), 6);

            var b = a.Clone();
            b.Set(Genes.SteelTarget, Genes.Spec(Genes.SteelTarget).Max);
            Assert.True(a.DistanceTo(b) > 0f);
        }
    }
}
