using System;
using System.Collections.Generic;
using AutoColony.Goals;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// An in-game harness for the power chain, run with <c>AUTOCOLONY_POWERTEST=1</c>.
    ///
    /// The chain is the hardest part of the director to observe: it needs a colony that has
    /// survived long enough to be fed, sheltered, stocked and researched before any of it can
    /// begin, which is hours of real time and a different colony every run. So this asks the
    /// question directly instead. It hands the real planner a set of hand-built colony states
    /// and records what it decides for each, against the real def database — the arbitration
    /// and the research walk are settled in seconds. A running commentary then reports the
    /// physical chain (generator, conduits, watts, coolers) as an ordinary run builds it.
    ///
    /// It cheats deliberately in the setup: research is wound back and then re-granted, and
    /// materials are dropped, because neither the grind nor the hauling is what is under test.
    /// Everything downstream of that is left entirely to the director.
    ///
    /// Gated on the environment variable so it is inert in normal play — without it,
    /// <see cref="GameComponentTick"/> returns on its first line.
    /// </summary>
    public class PowerChainSelfTest : GameComponent
    {
        public static bool Enabled
        {
            get { return Environment.GetEnvironmentVariable("AUTOCOLONY_POWERTEST") == "1"; }
        }

        bool granted;
        bool probed;
        bool cleared;
        int lastReportTick = -99999;

        public PowerChainSelfTest(Game game) { }

        public override void GameComponentTick()
        {
            if (!Enabled) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (!granted) granted = TryGrant(map);
            if (granted && !probed) { probed = true; RunProbes(map); RunWiringProbe(map); }
            if (probed && !cleared) cleared = TryClearShortTerm(map);

            int tick = Find.TickManager.TicksGame;
            if (tick - lastReportTick < 2500) return;
            lastReportTick = tick;
            Report(map);
        }

        /// <summary>
        /// Satisfies the short-term goals outright — beds and a food buffer — so the plan reaches
        /// its long-term horizon in minutes rather than in-game weeks. Neither is under test; the
        /// colony being slow to lay walls is what makes the power chain unreachable in practice,
        /// and it is exactly what a long run has always died of before getting here.
        ///
        /// Waits for a stockpile, because food only counts towards <c>daysOfFood</c> once it is
        /// in one — <c>ResourceCounter</c> ignores anything lying on the ground.
        /// </summary>
        static bool TryClearShortTerm(Map map)
        {
            Zone_Stockpile stockpile = null;
            var zones = map.zoneManager != null ? map.zoneManager.AllZones : null;
            for (int i = 0; zones != null && i < zones.Count; i++)
            {
                stockpile = zones[i] as Zone_Stockpile;
                if (stockpile != null && stockpile.Cells.Count > 0) break;
                stockpile = null;
            }
            if (stockpile == null) return false;

            var mealDef = AcDefs.Thing("MealSurvivalPack");
            int placed = 0;
            var cells = stockpile.Cells;
            for (int i = 0; i < cells.Count && placed < 8; i++)
            {
                if (cells[i].GetFirstItem(map) != null) continue;
                var meal = ThingMaker.MakeThing(mealDef, null);
                meal.stackCount = mealDef.stackLimit;
                GenSpawn.Spawn(meal, cells[i], map);
                meal.SetForbidden(false, false);
                placed++;
            }

            int beds = SpawnBeds(map, 4);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST: cleared short-term goals — {0} meal stacks into the stockpile, {1} beds",
                placed, beds));
            return true;
        }

        static int SpawnBeds(Map map, int count)
        {
            var bedDef = AcDefs.Bed;
            if (bedDef == null) return 0;

            var wood = AcDefs.Thing("WoodLog");
            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            int placed = 0;
            foreach (var cell in GenRadial.RadialCellsAround(origin, 20, true))
            {
                if (placed >= count) break;
                if (!GenSpawn.CanSpawnAt(bedDef, cell, map, Rot4.North)) continue;

                var bed = ThingMaker.MakeThing(bedDef, wood);
                GenSpawn.Spawn(bed, cell, map, Rot4.North);
                bed.SetFaction(Faction.OfPlayer);
                placed++;
            }
            return placed;
        }

        // ---------------------------------------------------------------- setup

        static bool TryGrant(Map map)
        {
            var rm = Find.ResearchManager;
            if (rm == null)
            {
                Chronicle.Record(ChronicleCategory.System, "SELFTEST: no ResearchManager yet, retrying");
                return false;
            }

            var electricity = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Electricity");
            if (electricity == null)
            {
                Chronicle.Record(ChronicleCategory.System, "SELFTEST: no Electricity def — aborting grant");
                return true;
            }

            if (!electricity.IsFinished)
            {
                rm.FinishProject(electricity, false, null, false);
                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SELFTEST: granted Electricity (now finished={0})", electricity.IsFinished));
            }
            else
            {
                Chronicle.Record(ChronicleCategory.System, "SELFTEST: Electricity was already finished");
            }

            var spot = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            Drop(map, spot, "Steel", 600);
            Drop(map, spot, "ComponentIndustrial", 20);
            Drop(map, spot, "WoodLog", 600);
            Drop(map, spot, "MealSurvivalPack", 60);
            Chronicle.Record(ChronicleCategory.System, "SELFTEST: dropped materials near " + spot);
            return true;
        }

        static void Drop(Map map, IntVec3 near, string defName, int count)
        {
            var def = AcDefs.Thing(defName);
            if (def == null) return;

            int perStack = def.stackLimit > 0 ? def.stackLimit : 75;
            int remaining = count;

            foreach (var cell in GenRadial.RadialCellsAround(near, 12, true))
            {
                if (remaining <= 0) break;
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                if (cell.GetFirstItem(map) != null) continue;

                var thing = ThingMaker.MakeThing(def, null);
                thing.stackCount = remaining < perStack ? remaining : perStack;
                remaining -= thing.stackCount;

                GenSpawn.Spawn(thing, cell, map);
                thing.SetForbidden(false, false);
            }
        }

        // ---------------------------------------------------------------- probes

        /// <summary>
        /// Asks the planner what it would do in each of a set of constructed situations. These
        /// are the cases the power chain turns on, and none of them is reachable quickly by
        /// simply letting a colony run.
        /// </summary>
        static void RunProbes(Map map)
        {
            var rm = Find.ResearchManager;

            // A -quicktest colony starts with Electricity already finished, which hides the
            // whole point of the research steering. Wind it back so the first round sees the
            // tech tree a real colony actually starts from.
            if (rm != null)
            {
                rm.ResetAllProgress();
                Chronicle.Record(ChronicleCategory.System, "SELFTEST: reset all research");
            }
            ProbeRound(map, "nothing researched");

            var electricity = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Electricity");
            if (rm != null && electricity != null)
            {
                rm.FinishProject(electricity, false, null, false);
                Chronicle.Record(ChronicleCategory.System, "SELFTEST: granted Electricity");
            }
            ProbeRound(map, "electricity done");
        }

        static void ProbeRound(Map map, string round)
        {
            Chronicle.Record(ChronicleCategory.System, "SELFTEST: ---- probes (" + round + ") ----");

            Probe(map, round, "fresh colony, nothing built",
                  s => { s.daysOfFood = 0f; },
                  Everything(false));

            Probe(map, round, "fed and sheltered, no power at all",
                  s => { },
                  Everything(true));

            Probe(map, round, "power room reserved but no generator running",
                  s => { },
                  Everything(true, RoomRole.Power));

            Probe(map, round, "generator running, no cooler",
                  s => { s.generators = 1; s.workingGenerators = 1; s.powerOutput = 1000f; },
                  Everything(true, RoomRole.Power));

            Probe(map, round, "generator running, freezer room built, cooler dead",
                  s => { s.generators = 1; s.workingGenerators = 1; s.powerOutput = 1000f; },
                  Everything(true, RoomRole.Power, RoomRole.Freezer));

            Probe(map, round, "solar panel built but roofed, so producing nothing",
                  s => { s.generators = 1; s.workingGenerators = 0; s.powerOutput = 0f; },
                  Everything(true, RoomRole.Power));
        }

        /// <summary>A layout with the short-term rooms plus whatever extras a probe names.</summary>
        static BaseLayout Everything(bool comfortable, params RoomRole[] extras)
        {
            var layout = new BaseLayout();
            layout.established = true;

            if (comfortable)
            {
                AddRoom(layout, RoomRole.Storage);
                AddRoom(layout, RoomRole.Kitchen);
                AddRoom(layout, RoomRole.Bedroom);
                AddRoom(layout, RoomRole.Workshop);   // satisfies Masonry, which is fire-risk driven
            }
            for (int i = 0; i < extras.Length; i++) AddRoom(layout, extras[i]);
            return layout;
        }

        static void AddRoom(BaseLayout layout, RoomRole role)
        {
            var room = new PlannedRoom();
            room.role = role;
            room.wallsQueued = true;
            room.furnitureQueued = true;
            layout.rooms.Add(room);
        }

        static void Probe(Map map, string round, string label, Action<ColonyState> shape, BaseLayout layout)
        {
            var state = new ColonyState();
            state.map = map;
            state.colonists = 3;
            state.colonistBeds = 3;
            state.daysOfFood = 20f;
            state.steel = 600;
            state.components = 20;
            state.wealthTotal = 20000f;
            state.itemsOutdoors = 0;
            shape(state);

            var ctx = new DirectorContext();
            ctx.map = map;
            ctx.state = state;
            ctx.layout = layout;
            ctx.genome = StrategyGenome.Default();

            var plan = new GoalPlanner().Plan(ctx);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST probe ({0}) [{1}] -> focus={2}  wanted={3}  research={4}",
                round, label,
                plan.Focus != null ? plan.Focus.Name : "none",
                plan.Wanted != null ? plan.Wanted.Name : "none",
                plan.ResearchWanted ?? "none"));
        }

        // ---------------------------------------------------------------- wiring probe

        /// <summary>
        /// Verifies the physical half of the chain without needing the colony to build it.
        ///
        /// Whether a generator produces once fuelled, whether `PowerModule` runs conduit to a
        /// stranded consumer, and whether that consumer ends up on a grid are three questions a
        /// colony answers only after surviving several days of raids — and the test colonies
        /// kept arriving here with two colonists and no time to build. So this stands a fuelled
        /// generator and a stranded consumer on the map directly and lets the module do the rest.
        /// The wiring itself is not faked; that is the part under test.
        /// </summary>
        static void RunWiringProbe(Map map)
        {
            var generatorDef = AcDefs.WoodFiredGenerator;
            var consumerDef = AcDefs.ElectricStove;
            if (generatorDef == null || consumerDef == null) return;

            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            var generator = PlaceWorking(map, generatorDef, origin, 6, 14);
            var consumer = PlaceWorking(map, consumerDef, origin, 18, 26);

            if (generator == null || consumer == null)
            {
                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SELFTEST wiring probe: could not place {0}{1} — skipped",
                    generator == null ? "generator " : "", consumer == null ? "consumer" : ""));
                return;
            }

            // A wood-fired generator with an empty hopper produces nothing, which is the same
            // failure as a roofed solar panel. Fill it, so what is being measured is the wiring.
            var refuelable = generator.TryGetComp<CompRefuelable>();
            if (refuelable != null) refuelable.Refuel(refuelable.Props.fuelCapacity);

            var generatorPower = generator.TryGetComp<CompPowerTrader>();
            var consumerPower = consumer.TryGetComp<CompPowerTrader>();

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST wiring probe: generator at {0} ({1:0}W, fuel {2:0}), consumer at {3} " +
                "{4} cells away, on a grid: {5}",
                generator.Position,
                generatorPower != null ? generatorPower.PowerOutput : 0f,
                refuelable != null ? refuelable.Fuel : 0f,
                consumer.Position,
                (consumer.Position - generator.Position).LengthHorizontal.ToString("0"),
                consumerPower != null && consumerPower.PowerNet != null));
        }

        /// <summary>Stands a finished, player-owned building on the first spot that will take it.</summary>
        static Thing PlaceWorking(Map map, ThingDef def, IntVec3 origin, int minDist, int maxDist)
        {
            foreach (var cell in GenRadial.RadialCellsAround(origin, maxDist, true))
            {
                if ((cell - origin).LengthHorizontal < minDist) continue;
                if (!cell.InBounds(map)) continue;
                if (!GenSpawn.CanSpawnAt(def, cell, map, Rot4.North)) continue;
                if (cell.Roofed(map)) continue;

                var thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                GenSpawn.Spawn(thing, cell, map, Rot4.North);
                thing.SetFaction(Faction.OfPlayer);
                return thing;
            }
            return null;
        }

        // ---------------------------------------------------------------- commentary

        static void Report(Map map)
        {
            var s = ColonyState.Capture(map);

            int conduits = 0, generatorBlueprints = 0;
            var conduitDef = AcDefs.PowerConduit;
            var generatorDef = AcDefs.WoodFiredGenerator;

            if (conduitDef != null && map.listerThings != null)
                conduits = map.listerThings.ThingsOfDef(conduitDef).Count;

            var pending = new List<Thing>();
            if (map.listerThings != null)
            {
                pending.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint));
                pending.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            }
            for (int i = 0; i < pending.Count; i++)
            {
                var d = pending[i].def != null ? pending[i].def.entityDefToBuild : null;
                if (d != null && generatorDef != null && d.defName == generatorDef.defName)
                    generatorBlueprints++;
            }

            // A wood-fired generator nobody has refuelled is the same class of failure as a
            // roofed solar panel, so the fuel level belongs in the report next to the watts.
            var fuel = new System.Text.StringBuilder();
            if (generatorDef != null && map.listerThings != null)
            {
                var built = map.listerThings.ThingsOfDef(generatorDef);
                for (int i = 0; i < built.Count; i++)
                {
                    var refuelable = built[i].TryGetComp<CompRefuelable>();
                    if (fuel.Length > 0) fuel.Append('/');
                    fuel.Append(refuelable != null ? refuelable.Fuel.ToString("0") : "n-a");
                }
            }

            float rain = map.weatherManager != null ? map.weatherManager.RainRate : 0f;

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST day {0}: generators {1} ({2} running, {3:0}W, fuel {4}), coolers {5}, " +
                "conduits {6}, generator blueprints {7}, unpowered {8}, wood {9}, " +
                "unroofed-powered {10}, rain {11:0.00}, fire risk {12:0.00}",
                s.day, s.generators, s.workingGenerators, s.powerOutput,
                fuel.Length > 0 ? fuel.ToString() : "-",
                s.workingCoolers, conduits, generatorBlueprints, s.unpoweredBuildings, s.wood,
                s.unroofedPowered, rain, FireRisk.Assess(map, s)));
        }
    }
}
