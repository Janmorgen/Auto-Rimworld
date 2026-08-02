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

            int beds = HarnessSetup.SpawnBeds(map, 4);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST: cleared short-term goals — {0} meal stacks into the stockpile, {1} beds",
                placed, beds));
            return true;
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

            var spot = HarnessSetup.ColonistOrigin(map);

            HarnessSetup.Scatter(map, spot, "Steel", 600);
            HarnessSetup.Scatter(map, spot, "ComponentIndustrial", 20);
            HarnessSetup.Scatter(map, spot, "WoodLog", 600);
            HarnessSetup.Scatter(map, spot, "MealSurvivalPack", 60);
            Chronicle.Record(ChronicleCategory.System, "SELFTEST: dropped materials near " + spot);
            return true;
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

            // --- states a live colony reaches too rarely to test by playing -----------------
            //
            // A -quicktest colony runs about a week in a temperate biome, so it never sees a
            // freezing winter and can never see toxic fallout at all — the game will not raise
            // it before day 60. These probe the decisions directly instead of waiting.

            Probe(map, round, "no fields planted at all",
                  s => { s.growingCells = 0; s.distinctCrops = 0; },
                  Everything(true));

            Probe(map, round, "one big field of a single crop, which one blight would empty",
                  s => { s.distinctCrops = 1; },
                  Everything(true));

            Probe(map, round, "hard freeze, nobody dressed for it",
                  s => { s.outdoorTemperature = -20f; s.colonistsUnderdressed = 3;
                         s.worstClothingGap = 36f; },
                  Everything(true));

            Probe(map, round, "hard freeze, but everyone is in parkas",
                  s => { s.outdoorTemperature = -20f; },
                  Everything(true));

            Probe(map, round, "heat wave, nobody dressed for it",
                  s => { s.outdoorTemperature = 42f; s.colonistsUnderdressed = 3;
                         s.worstClothingGap = 16f; },
                  Everything(true));

            Probe(map, round, "mild weather, so clothing should not be the focus",
                  s => { s.outdoorTemperature = 20f; },
                  Everything(true));

            ProbeConditions(map, round);
        }

        /// <summary>
        /// The map-wide conditions, which are judged by a policy rather than by the planner, so
        /// they are checked against that policy directly.
        ///
        /// Toxic fallout is the one that matters and the one that can never be reached in a test
        /// colony: the game will not raise it before day 60 and these runs end around day seven.
        /// </summary>
        static void ProbeConditions(Map map, string round)
        {
            var quiet = new Conditions.ActiveConditions();
            var fallout = new Conditions.ActiveConditions();
            fallout.toxicFallout = true;
            var flare = new Conditions.ActiveConditions();
            flare.solarFlare = true;

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST: conditions ({0}) — quiet: outdoors dangerous {1}, gathering suspended {2}",
                round,
                Conditions.ConditionResponse.OutsideIsDangerous(quiet),
                Conditions.ConditionResponse.SuspendElectiveOutdoorWork(quiet)));

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST: conditions ({0}) — toxic fallout: outdoors dangerous {1}, gathering " +
                "suspended {2}, crops at risk {3}",
                round,
                Conditions.ConditionResponse.OutsideIsDangerous(fallout),
                Conditions.ConditionResponse.SuspendElectiveOutdoorWork(fallout),
                Conditions.ConditionResponse.CropsAtRisk(fallout)));

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST: conditions ({0}) — solar flare: power out {1}, but outdoors dangerous {2}",
                round,
                Conditions.ConditionResponse.PowerIsOut(flare),
                Conditions.ConditionResponse.OutsideIsDangerous(flare)));
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

            // A default probe is a colony with nothing wrong with it *except* what the probe
            // names. Both of these are new short-term goals, and short term outranks everything
            // long term — so leaving the fields empty and the thermometer at zero would have
            // quietly hijacked every power and refrigeration probe in this file into "plant
            // something" and "make a coat", and they would still have passed while testing
            // nothing they were written for.
            state.growingCells = 3 * 60;
            state.distinctCrops = 2;
            state.outdoorTemperature = 20f;

            // Real colonists from the harness map, because some goals are judged on the people
            // and not on the tallies. Fortify weighs the colony's fighting strength against the
            // raid its wealth is summoning, and `ColonyStrength` reads pawns — so an empty list
            // meant strength zero, readiness zero, and maximum urgency in every probe. Fortify
            // duly took over the long-term horizon from Refrigeration across the whole file,
            // which is a property of the fixture and nothing to do with the director.
            //
            // Twice in one session a new rule has been silently rerouted by fixture defaults.
            // Anything added here that reads a new part of the state needs a default here too.
            var pawns = map.mapPawns != null ? map.mapPawns.FreeColonistsSpawned : null;
            for (int i = 0; pawns != null && i < pawns.Count; i++)
            {
                state.allColonists.Add(pawns[i]);
                if (!pawns[i].Downed) state.ableColonists.Add(pawns[i]);
            }

            shape(state);
            // Kept consistent with whatever the probe set, since these are derived readings.
            state.coldShortfall = ColonyState.ComfortableMin - state.outdoorTemperature;
            if (state.coldShortfall < 0f) state.coldShortfall = 0f;
            state.heatExcess = state.outdoorTemperature - ColonyState.ComfortableMax;
            if (state.heatExcess < 0f) state.heatExcess = 0f;

            var ctx = new DirectorContext();
            ctx.map = map;
            ctx.state = state;
            ctx.layout = layout;
            ctx.genome = StrategyGenome.Default();

            var planner = new GoalPlanner();
            var plan = planner.Plan(ctx);

            // The runner-up matters as much as the winner.
            //
            // These probes are the only tool for checking arbitration, and for anything
            // long-term they could not do it: two runs of identical code disagreed on four of
            // twenty-four probes. Long-term goals separate on urgency alone and Fortify reads
            // its urgency straight off the map's fire risk, so whichever colony the quicktest
            // happened to spawn decided the winner — and a flipped coin was indistinguishable
            // from a regression.
            //
            // Printing what it was choosing between makes the difference readable. A margin of
            // two hundred points is an arbitration rule; a margin of two is the weather.
            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SELFTEST probe ({0}) [{1}] -> focus={2}  wanted={3}  research={4}  ranked: {5}",
                round, label,
                plan.Focus != null ? plan.Focus.Name : "none",
                plan.Wanted != null ? plan.Wanted.Name : "none",
                plan.ResearchWanted ?? "none",
                planner.RankingFor(ctx, 3)));
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

            var origin = HarnessSetup.ColonistOrigin(map);

            var generator = HarnessSetup.PlaceFinished(map, generatorDef, origin, 6, 14);
            var consumer = HarnessSetup.PlaceFinished(map, consumerDef, origin, 18, 26);

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
