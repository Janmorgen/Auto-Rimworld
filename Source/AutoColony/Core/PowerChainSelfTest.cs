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
        int lastReportTick = -99999;

        public PowerChainSelfTest(Game game) { }

        public override void GameComponentTick()
        {
            if (!Enabled) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            if (!granted) granted = TryGrant(map);
            if (granted && !probed) { probed = true; RunProbes(map); }

            int tick = Find.TickManager.TicksGame;
            if (tick - lastReportTick < 2500) return;
            lastReportTick = tick;
            Report(map);
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

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST day {0}: generators {1} ({2} running, {3:0}W), coolers {4}, " +
                "conduits {5}, generator blueprints {6}, unpowered {7}",
                s.day, s.generators, s.workingGenerators, s.powerOutput, s.workingCoolers,
                conduits, generatorBlueprints, s.unpoweredBuildings));
        }
    }
}
