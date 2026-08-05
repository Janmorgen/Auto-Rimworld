using System;
using System.Linq;
using AutoColony.Connections;
using Xunit;

namespace AutoColony.Tests
{
    /// <summary>
    /// The checks that stop the connection map from lying.
    ///
    /// A map of how the director hangs together is only worth having if it cannot quietly drift
    /// from the director. These are the parts of that guarantee expressible offline; the other
    /// half — that every non-world name is a real ColonyState field — runs in game against the
    /// real type, because ColonyState touches Map and cannot be compiled here.
    /// </summary>
    public class TouchesTests
    {
        [Fact]
        public void EveryPrefixedNameIsInTheClosedWorldVocabulary()
        {
            // A typo in "world.drafted" would otherwise produce an edge that never matches
            // anything and a map that quietly shows two systems as unconnected.
            foreach (var name in Touches.AllNames())
            {
                if (!name.Contains(".")) continue;
                Assert.True(Touches.IsWorldEffect(name),
                    "'" + name + "' looks like a world effect but is not in WorldEffects");
            }
        }

        [Fact]
        public void EveryWorldEffectIsActuallyUsed()
        {
            // The reverse drift: a vocabulary entry nothing declares is either a missing
            // declaration or a name that has gone away.
            var used = Touches.AllNames();
            foreach (var effect in Touches.WorldEffects)
                Assert.True(used.Contains(effect),
                    "'" + effect + "' is in the vocabulary but no module or chain uses it");
        }

        [Fact]
        public void NoModuleIsDeclaredTwice()
        {
            var names = Touches.Modules.Select(m => m.module).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        [Fact]
        public void EveryObservedChainCitesItsEvidence()
        {
            // Observed means "seen in a chronicle", and the difference between that and a guess
            // is the quote. Without this the two collapse into each other within a week.
            foreach (var chain in Touches.Chains)
            {
                Assert.False(string.IsNullOrEmpty(chain.evidence),
                    chain.from + " -> " + chain.to + " has no evidence");

                if (chain.confidence != Confidence.Observed) continue;
                Assert.True(chain.evidence.Contains("run "),
                    "Observed edge " + chain.from + " -> " + chain.to +
                    " must cite the run it was seen in");
            }
        }

        [Fact]
        public void ChainsConnectNamesSomethingActuallyDeclares()
        {
            // A chain from or to a name no module reads or affects is an edge to nowhere.
            var declared = Touches.Modules
                .SelectMany(m => m.reads.Concat(m.affects))
                .Distinct().ToList();

            foreach (var chain in Touches.Chains)
                Assert.True(declared.Contains(chain.from) || declared.Contains(chain.to),
                    "chain " + chain.from + " -> " + chain.to +
                    " touches nothing any module declares");
        }

        [Fact]
        public void TheRun135ChainIsRepresented()
        {
            // The fault that cost three colonies: drafting held a colonist that bleeding
            // casualties needed. If this edge ever disappears from the map, the map has lost
            // the most expensive thing it knows.
            Assert.Contains(Touches.Chains, c =>
                c.from == "world.drafted" &&
                c.to == "colonistsBleedingOut" &&
                c.confidence == Confidence.Observed);
        }
    }
}
