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
    /// Two of fifteen are done. The rest are absent rather than guessed.
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
            "plan.goals"              // which goal holds the plan, and for how long
        };

        public static readonly Touch[] Modules =
        {
            new Touch
            {
                module = "DefenseModule",
                reads = new[]
                {
                    "ableColonists", "allColonists", "colonistBeds", "colonistsBleedingOut",
                    "colonistsDowned", "danger", "fires", "firesNearBase", "hostilePawns",
                    "huntedColonists", "nearestFireDistance", "poweredTurrets",
                    "predatorsHunting", "wealthTotal"
                },
                affects = new[]
                {
                    "world.drafted", "world.pawnPosition", "world.attackOrders",
                    "world.blueprints", "world.labourAvailable", "learning.threatMemory"
                }
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
