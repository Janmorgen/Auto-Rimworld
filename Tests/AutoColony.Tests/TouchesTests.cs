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
            // Keyed on the WRITERS, not the effect name.
            //
            // It was keyed on the name, and that made it blind in exactly the way it exists to
            // catch: world.blueprints was marked known for the Defense/Upkeep pair, and when
            // BasePlannerModule was declared as a third writer of the same effect the test went
            // on passing without a word. A detector that stops looking once an effect has been
            // explained once is a detector that only ever finds the first instance.
            var known = new HashSet<string>
            {
                "world.bills: WorkPriorityModule, ProductionModule",
                // Four now. Resource mines for steel and hunts; BasePlanner clears boulders out
                // of wall lines; Incident answers events; Upkeep mines a boulder out of a room.
                // None can double-designate — every one checks DesignationAt or DesignationOn —
                // but all four draw down the same miner and hunter hours and none of them sizes
                // its budget against the others' queues. Suspected contention on labour, not on
                // cells, and still unsettled by a run.
                "world.designations: ResourceModule, BasePlannerModule, IncidentModule, UpkeepModule",

                // Equipment unforbids a weapon so somebody will pick it up; ItemPolicy forbids
                // and unforbids by danger and by what the plan wants. Both write the same flag on
                // the same things, and the ordering between them is not stated anywhere. Nothing
                // has been seen to go wrong, and this is the pair to look at first if a colonist
                // ever stands next to a weapon it will not take.
                "world.forbidden: EquipmentModule, ItemPolicyModule",
                "world.labourAvailable: DefenseModule, WorkPriorityModule",

                // Three writers, and the third changes the answer. The note above used to read
                // "Defense places sandbags, Upkeep places furniture, disjoint kinds" and that was
                // true of two of them. BasePlannerModule also places furniture, and #64 is
                // UpkeepModule removing beds that the planner puts back — a bed built and pulled
                // out twice a day, which is this pair and not the sandbag one.
                // FOUR writers, and the list is derived rather than remembered — this entry
                // was written by hand as three and the test named PowerModule within seconds,
                // which is the argument for keying on the set instead of the name.
                //
                // Defense places sandbags and walls. Power places conduits. Those two are
                // disjoint from everything and from each other. BasePlanner and Upkeep BOTH
                // place furniture, and that pair is #64: a bed the planner puts in and upkeep
                // pulls out as surplus, twice a day, an oscillation whose own code comment
                // records two previous repairs.
                "world.blueprints: DefenseModule, BasePlannerModule, PowerModule, UpkeepModule",
            };

            foreach (var contested in Touches.ContestedEffects())
                Assert.True(known.Contains(contested),
                    "the writers of a contested effect changed and nobody has said whether it " +
                    "is still safe: " + contested);
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
