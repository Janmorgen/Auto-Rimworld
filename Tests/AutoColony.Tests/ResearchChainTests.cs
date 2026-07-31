using System.Collections.Generic;
using AutoColony.Goals;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The prerequisite walk that connects a goal to the research it is blocked on.
    ///
    /// Worth testing offline because the real failure it exists to prevent is invisible: the
    /// planner held "Power" as its focus while research worked elsewhere, and nothing in the
    /// game logged that the two were unrelated. The vanilla shape of the chain — refrigeration
    /// wanting air conditioning, which wants electricity — is the first case below.
    /// </summary>
    public class ResearchChainTests
    {
        /// <summary>A stand-in tech tree: project name to its prerequisites.</summary>
        sealed class Tree
        {
            readonly Dictionary<string, List<string>> edges = new Dictionary<string, List<string>>();
            readonly HashSet<string> finished = new HashSet<string>();

            public Tree Add(string project, params string[] prerequisites)
            {
                edges[project] = new List<string>(prerequisites);
                return this;
            }

            public Tree Finish(string project)
            {
                finished.Add(project);
                return this;
            }

            public IList<string> PrerequisitesOf(string project)
            {
                List<string> list;
                return edges.TryGetValue(project, out list) ? list : null;
            }

            // Unknown projects report finished, matching how the planner treats a def the
            // database has never heard of.
            public bool IsFinished(string project)
            {
                return finished.Contains(project) || !edges.ContainsKey(project);
            }

            public string Startable(string target)
            {
                return ResearchChain.FirstStartable(target, PrerequisitesOf, IsFinished);
            }

            public string StartableOf(params string[] targets)
            {
                return ResearchChain.FirstStartableOf(targets, PrerequisitesOf, IsFinished);
            }
        }

        static Tree Vanilla()
        {
            return new Tree()
                .Add("Electricity")
                .Add("Batteries", "Electricity")
                .Add("AirConditioning", "Electricity")
                .Add("SolarPanels", "Electricity")
                .Add("Stonecutting");
        }

        [Fact]
        public void WantingRefrigerationResearchesElectricityFirst()
        {
            Assert.Equal("Electricity", Vanilla().Startable("AirConditioning"));
        }

        [Fact]
        public void OnceElectricityIsDoneTheGoalsOwnProjectIsNext()
        {
            Assert.Equal("AirConditioning", Vanilla().Finish("Electricity").Startable("AirConditioning"));
        }

        [Fact]
        public void AProjectWithNoPrerequisitesIsItsOwnAnswer()
        {
            Assert.Equal("Stonecutting", Vanilla().Startable("Stonecutting"));
        }

        [Fact]
        public void NothingToDoWhenTheTargetIsAlreadyFinished()
        {
            Assert.Null(Vanilla().Finish("AirConditioning").Startable("AirConditioning"));
        }

        [Fact]
        public void ADeepChainWalksAllTheWayDown()
        {
            var tree = new Tree()
                .Add("Foundation")
                .Add("Middle", "Foundation")
                .Add("Top", "Middle");

            Assert.Equal("Foundation", tree.Startable("Top"));
        }

        [Fact]
        public void UnknownProjectsAreTreatedAsDoneRatherThanBlocking()
        {
            // A goal naming research from a DLC that is not installed must degrade to "nothing
            // to research", not to a prerequisite the colony can never satisfy.
            Assert.Null(Vanilla().Startable("SomeDlcProject"));
        }

        [Fact]
        public void AnUninstalledPrerequisiteDoesNotBlockTheProjectBehindIt()
        {
            var tree = new Tree().Add("Wanted", "NotInstalled");
            Assert.Equal("Wanted", tree.Startable("Wanted"));
        }

        [Fact]
        public void ACycleDegradesInsteadOfHanging()
        {
            var tree = new Tree()
                .Add("A", "B")
                .Add("B", "A");

            Assert.Null(tree.Startable("A"));
        }

        [Fact]
        public void SeveralTargetsTakeTheFirstThatYieldsSomething()
        {
            var tree = Vanilla().Finish("Stonecutting");
            Assert.Equal("Electricity", tree.StartableOf("Stonecutting", "Batteries"));
        }

        [Fact]
        public void NoTargetsMeansNothingToSteerTowards()
        {
            Assert.Null(Vanilla().StartableOf());
            Assert.Null(ResearchChain.FirstStartableOf(null, s => null, s => true));
        }

        [Fact]
        public void MissingLookupsAreToleratedRatherThanThrowing()
        {
            Assert.Null(ResearchChain.FirstStartable("Anything", null, s => false));
            Assert.Null(ResearchChain.FirstStartable("Anything", s => null, null));
            Assert.Null(ResearchChain.FirstStartable(null, s => null, s => false));
        }

        [Fact]
        public void TheFirstUnmetPrerequisiteDecidesTheBranch()
        {
            // Two unmet prerequisites: the goal declared them in an order, and that order is
            // what the colony follows rather than picking whichever happens to be cheaper.
            var tree = new Tree()
                .Add("First")
                .Add("Second")
                .Add("Both", "First", "Second");

            Assert.Equal("First", tree.Startable("Both"));
            Assert.Equal("Second", tree.Finish("First").Startable("Both"));
        }
    }
}
