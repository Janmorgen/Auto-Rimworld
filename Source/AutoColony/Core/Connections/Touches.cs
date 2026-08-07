using System;
using System.Collections.Generic;

namespace AutoColony.Connections
{
    /// <summary>How much the project actually knows about an edge.</summary>
    public enum Confidence
    {
        /// <summary>Seen happening in a chronicle. <c>evidence</c> quotes the line.</summary>
        Observed,

        /// <summary>Reasoned but never witnessed. Somewhere to go looking, not a finding.</summary>
        Suspected
    }

    /// <summary>What one module reads and what it changes.</summary>
    public class Touch
    {
        public string module;
        public string[] reads = new string[0];
        public string[] affects = new string[0];
    }

    /// <summary>
    /// An edge that no shared name reveals: one module's effect changes what another reads,
    /// through the world rather than through a field either of them names.
    ///
    /// These are where the expensive faults live. Both ends are plainly visible in their own
    /// module and the link between them is written down nowhere, so each module is correct and
    /// the pair is not.
    /// </summary>
    public class Consequence
    {
        public string from;
        public string to;
        public Confidence confidence;
        public string evidence;
    }

    /// <summary>
    /// What each module acts on and what it affects, and the chains that run between them.
    ///
    /// Kept as a manifest rather than attributes on the module classes for a build reason that
    /// turns out to be the right design anyway: modules touch Map and Pawn, so they cannot be
    /// compiled into the test assembly, and a declaration that cannot be tested is the stale
    /// map this is meant to replace. Free of game types, so the consistency checks run offline.
    ///
    /// Names are either a public field of ColonyState — checked against the real type at
    /// startup, so a rename is caught rather than silently lying — or one of the world effects
    /// in <see cref="WorldEffects"/>, which is a closed list so that a typo fails a test.
    ///
    /// Incomplete by construction, and honestly so: a module is listed once its reads and
    /// affects have been derived from its source, never from memory of what it probably does.
    /// Nine of sixteen are done. The rest are absent rather than guessed.
    ///
    /// It has already paid for itself. <see cref="ContestedEffects"/> over the first six turned
    /// up four things written by more than one module, and the fourth was a live fault nobody
    /// had noticed: DefenseModule sized a fire front against colonists it had drafted itself.
    /// That was found by asking the declarations a question, not by losing a colony to it,
    /// which is the whole argument for keeping this file honest.
    /// </summary>
    public static class Touches
    {
        /// <summary>
        /// Effects on the game world that are not ColonyState fields. Closed, so a typo in a
        /// declaration is a failing test rather than an edge that silently never matches.
        /// </summary>
        public static readonly string[] WorldEffects =
        {
            "world.drafted",          // pawn.drafter.Drafted
            "world.pawnPosition",     // where a drafted colonist is sent
            "world.attackOrders",     // an explicit order to attack, as opposed to standing armed
            "world.blueprints",       // construction placed for colonists to build
            "world.designations",     // mine, cut, harvest, strip-roof
            "world.labourAvailable",  // hands free to do ordinary work, which drafting removes
            "learning.threatMemory",  // what a kind of fight has cost, carried between colonies
            "plan.goals",             // which goal holds the plan, and for how long
            "world.workPriorities",   // pawn.workSettings — who does what, and in what order
            "world.bills",            // standing orders on a bench
            "world.growingZones",     // what a zone is set to sow
            "world.stockpileFilters", // what a stockpile will accept
            "world.tradeDeals",       // silver and goods across a trader's counter
            "world.researchProject",  // what the colony is currently studying
            "plan.capabilityGaps"     // things the colony wants and cannot have, and for how long
        };

        public static readonly Touch[] Modules =
        {
            new Touch
            {
                module = "DefenseModule",
                reads = new[]
                {
                    "ableColonists", "allColonists", "colonistBeds", "colonistsBleedingOut",
                    "colonistsDowned", "colonistsFreeForWork", "danger", "fires",
                    "firesNearBase", "hostilePawns", "huntedColonists", "nearestFireDistance",
                    "poweredTurrets", "predatorsHunting", "ticksToFirstBloodLoss",
                    "wealthTotal"
                },
                affects = new[]
                {
                    "world.drafted", "world.pawnPosition", "world.attackOrders",
                    "world.blueprints", "world.labourAvailable", "learning.threatMemory"
                }
            },

            new Touch
            {
                module = "ResourceModule",
                reads = new[]
                {
                    "colonists", "colonistsDowned", "components", "conditions", "daysOfFood",
                    "firesNearBase", "fuelWanted", "hostilesNearBase", "steel",
                    "unbutcheredNutrition", "wood"
                },
                affects = new[] { "world.designations" }
            },

            new Touch
            {
                module = "ZoneModule",
                reads = new[]
                {
                    "allColonists", "colonists", "daysOfFood", "distinctCrops", "growingCells",
                    "growingSeasonNow", "medicineCount", "minMood", "tamedAnimals", "textiles"
                },
                affects = new[] { "world.growingZones", "world.stockpileFilters" }
            },

            new Touch
            {
                module = "WorkPriorityModule",
                reads = new[]
                {
                    "ableColonists", "allColonists", "colonistsIdle", "fuelWanted",
                    "pendingBlueprints", "pendingFrames", "plan.goals"
                },
                affects = new[] { "world.workPriorities", "world.bills", "world.labourAvailable" }
            },

            new Touch
            {
                module = "ProductionModule",
                reads = new[]
                {
                    "coldShortfall", "colonists", "heatExcess", "pendingBlueprints", "pendingFrames"
                },
                affects = new[] { "world.bills" }
            },

            new Touch
            {
                module = "TradeModule",
                reads = new[]
                {
                    "ableColonists", "colonists", "components", "daysOfFood", "medicineCount",
                    "silver", "steel", "textiles", "wood"
                },
                affects = new[] { "world.tradeDeals" }
            },

            // The two below read nothing the colony senses, and that is the declaration rather
            // than an omission.
            //
            // ResearchModule chooses what to study from the plan, the genome and the archive.
            // PowerModule decides what to build from scanning the map. Neither consults a single
            // ColonyState field — not daysOfFood, not coldShortfall, not colonistsDowned — so
            // nothing either of them does can respond to the colony being in trouble.
            //
            // Whether that is a fault depends on the module. Research being led by strategy
            // rather than by today's weather is defensible. Power being blind to coldShortfall
            // while UpkeepModule places heaters against it is harder to defend, and it is the
            // sort of gap this manifest exists to make visible rather than to settle here.

            new Touch
            {
                module = "ResearchModule",
                reads = new string[0],
                affects = new[] { "world.researchProject" }
            },

            new Touch
            {
                module = "PowerModule",
                reads = new string[0],
                affects = new[] { "world.blueprints", "plan.capabilityGaps" }
            },

            new Touch
            {
                module = "UpkeepModule",
                reads = new[]
                {
                    "allColonists", "buildingsWantingFuel", "colonistBeds", "colonists",
                    "cutOff", "fuelOnHand", "fuelStanding", "fuelStarved", "usableMaterial",
                    "wood", "workingGenerators"
                },
                affects = new[] { "world.blueprints", "world.designations" }
            }
        };

        /// <summary>
        /// The chains. Each one is a link the modules at either end could not see.
        /// </summary>
        public static readonly Consequence[] Chains =
        {
            new Consequence
            {
                from = "world.drafted",
                to = "world.labourAvailable",
                confidence = Confidence.Observed,
                evidence = "run 132 day 3 07h — \"holding off gathering: 0 fires, 1 hostiles " +
                           "and 1 down at the colony\": drafting withdraws hands from work by " +
                           "the colony's own account"
            },

            new Consequence
            {
                from = "world.labourAvailable",
                to = "world.blueprints",
                confidence = Confidence.Observed,
                evidence = "run 134 day 2 04h — \"not opening another room; 3 unfinished and " +
                           "only 2 allowed for 3 able colonists and 381 material\": fewer hands " +
                           "leaves blueprints standing unbuilt"
            },

            new Consequence
            {
                from = "world.blueprints",
                to = "colonistsUntended",
                confidence = Confidence.Observed,
                evidence = "run 135 day 4 06h — \"3 down and no bed would go up; laid a " +
                           "sleeping spot\": a bed that stays a blueprint is not a bed to " +
                           "carry a casualty to"
            },

            // The one that cost run 134 ten stool orders, end to end. Every link observed.
            new Consequence
            {
                from = "world.blueprints",
                to = "world.blueprints",
                confidence = Confidence.Observed,
                evidence = "run 134 days 2-3 — an unbuilt table cannot clear AteWithoutTable, " +
                           "so NoTable fires every pass and AddTable places another; each new " +
                           "table then draws one stool per colonist from SeatWhatNeedsSeating. " +
                           "A remedy whose own backlog re-triggers it. Fixed in 23dc388 by " +
                           "tallying colony-scoped wants colony-wide"
            },

            new Consequence
            {
                from = "world.blueprints",
                to = "colonistBeds",
                confidence = Confidence.Observed,
                evidence = "run 138 day 8 — a roofed but unenclosed room counted as a refuge, " +
                           "so WITHDRAWING read \"a room to hold, so the open is elective\" and " +
                           "the manhunter followed both colonists in. What the planner has " +
                           "finished decides whether withdrawing is a real option"
            },

            // The loop run 142 was inside at day 21. Every link observed in one chronicle.
            new Consequence
            {
                from = "world.blueprints",
                to = "world.labourAvailable",
                confidence = Confidence.Observed,
                evidence = "run 142 days 0-21 — no enclosed bedroom means SleptOutside and " +
                           "SleptOnGround at -4 each, mood falls to 0.04 at worst, a colonist " +
                           "breaks, able hands drop 3 to 2, and the planner's throttle tightens " +
                           "on the very room that would end it: \"only 1 allowed for 2 able " +
                           "colonists\". The shortage of shelter causes the shortage of hands " +
                           "that prevents the shelter"
            },

            new Consequence
            {
                from = "world.blueprints",
                to = "fuelOnHand",
                confidence = Confidence.Observed,
                evidence = "run 143 day 10 — four passive coolers at fifty wood each while " +
                           "AddCooler stayed at severity 0.80 and roomsEver was 0, on a map " +
                           "with no tree standing anywhere. Whether the planner has enclosed a " +
                           "room decides whether temperature work is spending or wasting"
            },

            // The dominant death mode of runs 132-144, stated as an edge.
            new Consequence
            {
                from = "world.blueprints",
                to = "colonistsDowned",
                confidence = Confidence.Observed,
                evidence = "run 144 day 3 — a manhunter pack put 3 of 4 down in one engagement " +
                           "with roomsEver at 0. Animals cannot open doors, so whether the " +
                           "planner has closed one room before the first pack decides whether " +
                           "the fight is survivable at all — and no score says so"
            },

            // Why no colony this session ever reached four colonists.
            new Consequence
            {
                from = "world.blueprints",
                to = "colonists",
                confidence = Confidence.Observed,
                evidence = "run 146, and every run back to 132 — \"left Ben where they fell — " +
                           "no capture: bed NONE\" " +
                           "on every downed raider. Capture needs a prisoner bed, a prisoner bed " +
                           "is never built without a prisoner, so recruitment fired zero times " +
                           "across fifteen colonies and all 23 arrivals were wanderers the game " +
                           "handed over. The precondition nothing creates"
            },

            // Two rules, each written for a real loss, deadlocking each other.
            new Consequence
            {
                from = "world.blueprints",
                to = "plan.goals",
                confidence = Confidence.Observed,
                evidence = "run 142 — the planner reserves a spare slot for the focus room but " +
                           "only once the plan asks twice running, while the focus detector " +
                           "demoted after half a day, so the plan could never ask twice. " +
                           "Shelter asked from hour zero, was stood down five times in four " +
                           "days, and got a bedroom on day 15"
            },

            // The season blindness reaching a capability that did not exist when it was found.
            new Consequence
            {
                from = "plan.goals",
                to = "world.blueprints",
                confidence = Confidence.Observed,
                evidence = "run 159 day 23-25 — the trade module bought food against " +
                           "FoodDaysPerColonist, a flat target with no season in it, so a " +
                           "colony in fall bought 2.2 days and was starving again two days " +
                           "later with nothing growing outdoors. Buying inherits the same " +
                           "blindness as sowing, because both read the same seasonless number"
            },

            new Consequence
            {
                from = "world.drafted",
                to = "colonistsBleedingOut",
                confidence = Confidence.Observed,
                evidence = "run 135 day 4 06h-10h — the last upright colonist held in a " +
                           "withdrawal it had already decided on while three bled to death " +
                           "with 25 medicine in store. Fixed in f79df1b: bleeding overrides " +
                           "the don't-empty-the-line clause"
            },

            new Consequence
            {
                from = "world.designations",
                to = "steel",
                confidence = Confidence.Suspected,
                evidence = "found by ContestedEffects, not by a run. UpkeepModule designates " +
                           "mining to clear boulders out of a wall line; ResourceModule " +
                           "designates mining for steel and components. Neither can " +
                           "double-designate a cell, but both queues draw on the same " +
                           "miner-hours and ResourceModule sizes its budget from its own " +
                           "additions this pass alone. A wall full of boulders can soak the " +
                           "mining a steel shortfall was counting on, and nothing reports the " +
                           "trade. To settle it: a run where a rocky site raises daysToSteel " +
                           "while pendingFrames stalls on wall segments"
            },

            new Consequence
            {
                from = "world.drafted",
                to = "firesNearBase",
                confidence = Confidence.Observed,
                evidence = "found by ContestedEffects over the declarations rather than by a " +
                           "colony, and confirmed in source: DefenseModule drafts against a " +
                           "raid, then sizes the fire front against ableColonists, which counts " +
                           "the colonists it has just drafted. A raid with incendiaries makes " +
                           "both conditions true from one event, so the front is judged " +
                           "fightable by people who cannot be sent. Same shape as run 135's " +
                           "world.drafted -> colonistsBleedingOut: drafting removes hands from " +
                           "everything that is not the fight, and only the fight knows it"
            },

            new Consequence
            {
                from = "world.designations",
                to = "learning.threatMemory",
                confidence = Confidence.Observed,
                evidence = "run 161 days 2-8 — seven muffalo designated across two passes " +
                           "produced three 'Muffalo revenge' incidents and three recorded " +
                           "Manhunter fights, " +
                           "the memory going 1.50x, 1.69x, 1.90x with two colonists downed. " +
                           "The hunt module manufactures the threats the threat memory learns " +
                           "from, which makes this the one edge in the map that is a loop"
            },

            new Consequence
            {
                from = "learning.threatMemory",
                to = "world.designations",
                confidence = Confidence.Observed,
                evidence = "run 161 — the return leg, and until this commit it did not exist. " +
                           "The colony drew precisely the right lesson from being mauled and " +
                           "the module choosing what to hunt could not hear it, so it kept " +
                           "buying the fights the lesson was about. The dangerous-prey floor " +
                           "is now ThreatMemory.ForceFor(Manhunter), with the old constant as " +
                           "the prior for a colony that has met none"
            },

            new Consequence
            {
                from = "world.designations",
                to = "colonistsBleedingOut",
                confidence = Confidence.Observed,
                evidence = "run 162 day 3 15h - day 4 07h — a hunt designation on a Rhinoceros " +
                           "(threat 270, taken at 528 strength) became a 'Rhinoceros revenge' " +
                           "eleven hours later that took Poole's right leg and put Pansy on the " +
                           "floor bleeding. Pansy died of blood loss at day 4 07h. The hunt " +
                           "module chooses what will later arrive as a casualty, and the two " +
                           "were connected by nothing"
            },

            new Consequence
            {
                from = "ticksToFirstBloodLoss",
                to = "world.drafted",
                confidence = Confidence.Observed,
                evidence = "run 162 day 4 01h-07h — the medic held back to tend was chosen on " +
                           "Medicine skill alone, and the best doctor had just lost a leg to the " +
                           "same fight that made the casualty. Six hours of walking against six " +
                           "hours of bleeding, decided by a comparison nothing was making. The " +
                           "deadline was read from the game every pass and thrown away, kept " +
                           "only as a count of who had one"
            },

            new Consequence
            {
                from = "world.drafted",
                to = "colonistsDowned",
                confidence = Confidence.Observed,
                evidence = "run 164 day 5 10h-11h — Simon went down, the colony judged the " +
                           "fight lost and withdrew two able colonists past him to the refuge, " +
                           "and a raider carried him off an hour later. NearestCarrier skips " +
                           "every drafted colonist because a work job breaks the draft, and " +
                           "during a withdrawal every colonist is drafted, so no rescue was " +
                           "possible at the one moment it mattered. Third colonist lost this " +
                           "way. Third instance of this same edge after run 135's bleeding and " +
                           "the fire front judged fightable by people already sent to a line"
            },

            new Consequence
            {
                from = "world.drafted",
                to = "daysOfFood",
                confidence = Confidence.Observed,
                evidence = "run 173 and run 174 — both lost with NeedFood at -32 and -26 beside " +
                           "23.0 and 30.7 days of food in store. Run 173 held its colonists " +
                           "drafted at a rally point for two days against a siege it correctly " +
                           "judged unwinnable. Confirmed from Humanlike.xml rather than " +
                           "inferred: the drafted branch of the think tree holds only " +
                           "JobGiver_MoveToStandable and JobGiver_Orders and sits above " +
                           "JobGiver_GetFood, including above the starving branch. A drafted " +
                           "colonist does not eat. Fourth instance of this edge, which makes " +
                           "it a property of drafting rather than three separate bugs"
            },

            new Consequence
            {
                from = "danger",
                to = "world.drafted",
                confidence = Confidence.Observed,
                evidence = "run 173 — the hold that keeps a draft alive resets whenever a " +
                           "hostile is within sixty cells, so hostiles camped at range held it " +
                           "with no upper bound at all, for two days, at danger None. Besieged() " +
                           "already detects exactly this shape and its own comment says they " +
                           "'sit at range, build mortars, and be resupplied for days' — and it " +
                           "was wired only to label the fight for ThreatMemory, never to the " +
                           "hold. The colony could tell a siege from an assault and the part " +
                           "that needed to know could not hear it"
            },

            new Consequence
            {
                from = "plan.goals",
                to = "colonistsBleedingOut",
                confidence = Confidence.Observed,
                evidence = "run 161-179, eighteen deaths across seven preserved colonies: " +
                           "fourteen of blood loss, and every fatal case was everybody down at " +
                           "once, after which no tending reaches anyone. Four colonies lost by " +
                           "day two. ShelterGoal was satisfied by beds and said nothing about " +
                           "whether a shell was closed, so the goal layer could report shelter " +
                           "complete while the colony had nowhere to withdraw to. RefugeGoal " +
                           "now asks the stricter question, sharing BasePlannerModule.Enclosed " +
                           "with DefenseModule.Refuge so the two cannot disagree"
            },

            new Consequence
            {
                from = "plan.goals",
                to = "world.workPriorities",
                confidence = Confidence.Observed,
                evidence = "run 166 day 2 — the plan's focus was 'Shelter everyone, 0 beds for " +
                           "3 colonists', a Kitchen and Bedroom had been sited since day 0, and " +
                           "the work lean read Tailoring 2.2, Growing 1.7, Cleaning 1.6, " +
                           "Cooking 1.5 with Construction nowhere in it. Construction keyed on " +
                           "pendingBlueprints alone, which is zero between batches, so it sat " +
                           "at 0.8 while three colonists slept outside. The goal layer named " +
                           "the most urgent thing in the colony and the labour layer had no " +
                           "way to hear it"
            }
        };

        /// <summary>
        /// Edges implied by a shared name: one module changes what another reads.
        ///
        /// Derived rather than declared, so it cannot disagree with the declarations it comes
        /// from — which is the whole reason the map is rendered instead of drawn.
        /// </summary>
        public static List<string> SharedNameEdges()
        {
            var edges = new List<string>();
            for (int i = 0; i < Modules.Length; i++)
                for (int j = 0; j < Modules.Length; j++)
                {
                    if (i == j) continue;
                    foreach (var effect in Modules[i].affects)
                        foreach (var read in Modules[j].reads)
                            if (effect == read)
                                edges.Add(Modules[i].module + " -> " + Modules[j].module +
                                          " (" + effect + ")");
                }
            return edges;
        }

        /// <summary>
        /// Things more than one module changes.
        ///
        /// SharedNameEdges finds where one module's effect is another's input, which is the
        /// ordinary way work flows. This finds the other shape: two modules writing the same
        /// thing, neither aware of the other. That is the contested-ownership row in goal.md's
        /// fault table, and it is what cost run 142 its bedroom — the planner repurposed the
        /// room the goal layer was still asking for, because taking a room for parts and taking
        /// it for a different label were the same act performed by different code.
        ///
        /// Two writers is not automatically a fault. It is a question that has to have an
        /// answer, and the answer has to be written down somewhere.
        /// </summary>
        public static List<string> ContestedEffects()
        {
            var contested = new List<string>();
            foreach (var effect in WorldEffects)
            {
                var writers = new List<string>();
                for (int i = 0; i < Modules.Length; i++)
                    if (System.Array.IndexOf(Modules[i].affects, effect) >= 0)
                        writers.Add(Modules[i].module);

                if (writers.Count > 1)
                    contested.Add(effect + ": " + string.Join(", ", writers.ToArray()));
            }
            return contested;
        }

        /// <summary>Every name used anywhere in the manifest, for the consistency checks.</summary>
        public static List<string> AllNames()
        {
            var names = new List<string>();
            foreach (var t in Modules)
            {
                foreach (var r in t.reads) if (!names.Contains(r)) names.Add(r);
                foreach (var a in t.affects) if (!names.Contains(a)) names.Add(a);
            }
            foreach (var c in Chains)
            {
                if (!names.Contains(c.from)) names.Add(c.from);
                if (!names.Contains(c.to)) names.Add(c.to);
            }
            return names;
        }

        /// <summary>Names that are world effects rather than ColonyState fields.</summary>
        public static bool IsWorldEffect(string name)
        {
            return Array.IndexOf(WorldEffects, name) >= 0;
        }
    }
}
