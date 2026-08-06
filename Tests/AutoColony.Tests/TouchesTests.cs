using System;
using System.Collections.Generic;
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
        public void EveryContestedEffectIsKnownAndAccountedFor()
        {
            // Two modules writing one thing is the contested-ownership fault, and the map
            // should surface it rather than leave it to be discovered by a dead colony. Not
            // every case is a bug — but every case is a question, and this is the list of
            // questions that currently have answers.
            //
            // world.bills: WorkPriorityModule queues refuelling and hauling work;
            //   ProductionModule sets crafting and cooking bills. Different benches, no overlap
            //   observed. Recorded so that if one starts clearing the other's bills it is a
            //   known suspect rather than a mystery.
            //
            // world.blueprints: DefenseModule places sandbags and walls, UpkeepModule places
            //   furniture. Disjoint kinds, and they coordinate through the world rather than
            //   through each other — GenConstruct.CanPlaceBlueprintAt refuses a contested cell
            //   at every one of the five placement sites, and UpkeepModule.CountInBase counts
            //   blueprints and frames as things that already exist, so a thing another module
            //   queued is seen as present rather than missing. Coordinating through shared
            //   world state is the right shape: it needs no module to know another exists.
            //
            // world.designations: ResourceModule designates mining for steel and components;
            //   UpkeepModule designates mining to clear a boulder out of a wall line. Neither
            //   can double-designate — both check DesignationAt/DesignationOn before adding —
            //   but the cells are not what they contend for. Both queues are drawn down by the
            //   same miner-hours, and ResourceModule sizes its budget from its own additions
            //   this pass only. A wall full of boulders can therefore soak the mining a steel
            //   shortfall was counting on, with nothing anywhere reporting the trade.
            //   Suspected, not observed — recorded as an edge so a run can settle it.
            //
            // world.labourAvailable: DefenseModule takes labour away by drafting;
            //   WorkPriorityModule hands it out. This one was a live fault when the detector
            //   first ran, and the reason to have written the detector: DefenseModule sized a
            //   fire front against ColonyState.ableColonists, which includes the drafted,
            //   having drafted them itself moments earlier. Fixed by giving the colony the
            //   sense it was missing — colonistsFreeForWork — rather than a rule about fires.
            //   The two modules still both write this, and that is correct: one supplies
            //   labour and one spends it. What was wrong was measuring the supply with a
            //   number that meant something else.
            var known = new HashSet<string> { "world.bills", "world.blueprints",
                                              "world.designations", "world.labourAvailable" };

            foreach (var contested in Touches.ContestedEffects())
            {
                var effect = contested.Substring(0, contested.IndexOf(':'));
                Assert.True(known.Contains(effect),
                    "a new contested effect appeared and nobody has said whether it is safe: " +
                    contested);
            }
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
