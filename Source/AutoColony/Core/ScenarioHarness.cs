using System;
using System.Collections.Generic;
using AutoColony.Plants;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Puts a specific situation in front of the director on demand, rather than waiting for one.
    ///
    /// A colony reaches its first raid in ten real minutes and its first *losing* raid maybe
    /// never — the one that killed a test colony arrived on day twelve, after thirty-seven
    /// minutes, and could not be asked for again. Combat, capture and rescue were all effectively
    /// untestable at that rate.
    ///
    /// The game's own incident workers will fire on request, so the scenario is set up in
    /// seconds and the behaviour under test is left entirely to the director. Set
    /// <c>AUTOCOLONY_SCENARIO</c> to one of:
    ///
    ///   raid:&lt;points&gt;   an enemy raid of a chosen size — 50 is a lone tribal, 2000 a wave
    ///   downed           a downed neutral stranger, to be rescued
    ///   downedhostile    a downed raider, to be captured
    ///   provision        food and material for a week, to reach the late plan without the wait
    ///   showcase         provision, plus one built example of every room the director knows,
    ///                    each reported against what it was expected to classify as
    ///   research         fed, housed and supplied, so the only thing left to watch is whether
    ///                    the planner gets a bench into a research room
    ///   casualty         as `research`, and then a colonist put on the floor with nothing
    ///                    burning — the case that killed run 38 four times over
    ///
    /// The last one is the mirror of `starve` and `strip`, and exists for the same reason in
    /// reverse. Those two take things away to see what the colony does without them; this hands
    /// everything over to see what it does when nothing is stopping it. Four colonies in a row
    /// died before ever finishing a research room, which is a slow way to test a placement loop
    /// that fails in a single pass.
    ///
    /// The reporting is the point as much as the trigger: it names what every colonist is
    /// actually *doing*, which is how "two were drafted and only one ever fought" was found.
    /// </summary>
    public class ScenarioHarness : GameComponent
    {
        static string Scenario
        {
            get { return Environment.GetEnvironmentVariable("AUTOCOLONY_SCENARIO"); }
        }

        /// <summary>Long enough for the director to have taken stock and settled.</summary>
        const int FireAtTick = 3000;

        bool fired;
        int lastReport = -9999;

        /// <summary>A bed waiting for its room to settle before it can be marked.</summary>
        Building_Bed pendingPrisonBed;
        int markAtTick;

        public ScenarioHarness(Game game) { }

        public override void GameComponentTick()
        {
            var scenario = Scenario;
            if (string.IsNullOrEmpty(scenario)) return;

            var map = Find.CurrentMap;
            if (map == null) return;

            int tick = Find.TickManager.TicksGame;
            if (!fired && tick >= FireAtTick)
            {
                fired = true;
                pendingPrisonBed = spawnedPrisonBed;
                Fire(map, scenario);
                pendingPrisonBed = spawnedPrisonBed;
                markAtTick = tick + 250;
            }

            if (pendingPrisonBed != null && tick >= markAtTick) MarkPendingPrisonBed();

            if (showcaseAt > 0 && tick >= showcaseAt)
            {
                showcaseAt = -1;
                ReportShowcase(map);
            }

            if (!fired || tick - lastReport < 300) return;
            lastReport = tick;
            Report(map);
        }

        // ---------------------------------------------------------------- setting it up

        static void Fire(Map map, string scenario)
        {
            try
            {
                if (scenario.StartsWith("raid"))
                {
                    float points = 500f;
                    int colon = scenario.IndexOf(':');
                    if (colon >= 0) float.TryParse(scenario.Substring(colon + 1), out points);
                    FireRaid(map, points);
                }
                else if (scenario == "downed") SpawnDownedStranger(map, hostile: false);
                else if (scenario == "downedhostile") SpawnDownedStranger(map, hostile: true);
                else if (scenario == "fire") StartFires(map);
                else if (scenario == "coldsnap") FireCondition(map, "ColdSnap");
                else if (scenario == "heatwave") FireCondition(map, "HeatWave");
                else if (scenario == "eclipse") FireCondition(map, "SolarFlare");
                else if (scenario == "corpse") KillAColonist(map);
                else if (scenario == "casualty") { ClearTheWayToResearch(map); DownAColonist(map); }
                else if (scenario == "manhunters") FireIncident(map, "ManhunterPack", 400f);
                else if (scenario == "infestation") FireIncident(map, "Infestation", 500f);
                else if (scenario == "starve") RemoveAllFood(map);
                else if (scenario == "provision") Provision(map);
                else if (scenario == "showcase") { Provision(map); Showcase(map); }
                else if (scenario == "livestock") { Provision(map); SpawnLivestock(map, 4); }
                else if (scenario == "plants") ClassifyPlants(map);
                else if (scenario == "seating") ClassifySeating(map);
                else if (scenario == "research") ClearTheWayToResearch(map);
                else if (scenario == "strip") Chronicle.Record(ChronicleCategory.System,
                    "SCENARIO: removed " + HarnessSetup.StripMaterials(map) + " building material");
                else Chronicle.Record(ChronicleCategory.System, "SCENARIO: unknown '" + scenario + "'");
            }
            catch (Exception e)
            {
                // The type and the top frame, not just the message — "object reference not set"
                // on its own names nothing and cost several runs of guessing.
                string where = e.StackTrace != null ? e.StackTrace.Split('\n')[0].Trim() : "?";
                Chronicle.Record(ChronicleCategory.System,
                    "SCENARIO failed: " + e.GetType().Name + " — " + e.Message + " @ " + where);
            }
        }

        /// <summary>
        /// Reports what the director thinks every sowable plant is for, against what it was
        /// expected to be.
        ///
        /// Same method as the room showcase, for the same reason: an understanding of rules that
        /// cannot be read is only worth something if it is written down before the answer
        /// arrives. The expectations below are the author's; where the game disagrees, the game
        /// is right and the classifier is wrong.
        /// </summary>
        static void ClassifyPlants(Map map)
        {
            var expected = new Dictionary<string, PlantRole>
            {
                { "Plant_Rice",        PlantRole.Food },
                { "Plant_Corn",        PlantRole.Food },
                { "Plant_Potato",      PlantRole.Food },
                { "Plant_Strawberry",  PlantRole.Food },
                { "Plant_Cotton",      PlantRole.Textile },
                { "Plant_Devilstrand", PlantRole.Textile },
                { "Plant_Healroot",    PlantRole.Medicine },
                { "Plant_Haygrass",    PlantRole.Fodder },
                { "Plant_Psychoid",    PlantRole.Social },
                { "Plant_Smokeleaf",   PlantRole.Social },
                { "Plant_Hops",        PlantRole.Social },
                { "Plant_Tinctoria",   PlantRole.Utility },
                { "Plant_Daylily",     PlantRole.Decorative },
                { "Plant_Rose",        PlantRole.Decorative },
                { "Plant_TreeCocoa",   PlantRole.Wood },
            };

            var sowable = PlantTaxonomy.Sowable();
            int agreed = 0, disagreed = 0;

            for (int i = 0; i < sowable.Count; i++)
            {
                var plant = sowable[i];
                var purpose = PlantTaxonomy.RoleOf(plant);
                var harvest = plant.plant.harvestedThingDef;

                PlantRole want;
                bool predicted = expected.TryGetValue(plant.defName, out want);

                string verdict;
                if (!predicted) verdict = "(no prediction)";
                else if (want == purpose) { verdict = "as predicted"; agreed++; }
                else { verdict = "PREDICTED " + want + " — WRONG"; disagreed++; }

                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "PLANTS {0,-20} -> {1,-11} harvest={2,-18} {3}",
                    plant.defName, purpose,
                    harvest != null ? harvest.defName : "nothing",
                    verdict));
            }

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "PLANTS {0} sowable plants classified — {1} as predicted, {2} not",
                sowable.Count, agreed, disagreed));
        }

        /// <summary>
        /// Reports which furniture the director believes is useless without a chair beside it.
        ///
        /// Predictions written before the answer arrives, as with the plants. This one earns the
        /// treatment more than most, because the discriminator is a *pair* of def fields and
        /// either one alone gives a plausible, wrong answer: <c>requireChair</c> defaults to true
        /// and so catches horseshoes and billiards, which are played standing; the worker class
        /// alone catches Game-of-Ur, which explicitly needs no chair. The list below should come
        /// out matching the set RimWorld itself ships an alert for, and if it does not, the
        /// classifier is wrong.
        /// </summary>
        static void ClassifySeating(Map map)
        {
            var expected = new Dictionary<string, bool>
            {
                { "ChessTable",     true  },   // Alert_ChessTableNoChairs
                { "PokerTable",     true  },   // Alert_PokerTableNoChairs
                { "Table1x2c",      true  },   // eaten at
                { "Table2x2c",      true  },
                { "TableLong2x2c",  true  },
                { "GameOfUrBoard",  false },   // requireChair explicitly false
                { "BilliardsTable", false },   // played standing
                { "HorseshoesPin",  false },
                { "HoopstoneRing",  false },
                { "Telescope",      false },   // interaction cell, not an adjacent seat
                { "DiningChair",    false },   // is a seat; does not need one
                { "Stool",          false },
                { "Bed",            false },
                { "TableButcher",   false },   // a worktable, not a surface
            };

            int agreed = 0, disagreed = 0;
            var all = DefDatabase<ThingDef>.AllDefsListForReading;

            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                bool want;
                if (!expected.TryGetValue(def.defName, out want)) continue;

                bool needs = Furniture.SeatingRule.NeedsAdjacentSeat(def);
                string verdict;
                if (needs == want) { verdict = "as predicted"; agreed++; }
                else { verdict = "PREDICTED " + want + " — WRONG"; disagreed++; }

                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SEATING {0,-16} needsSeat={1,-5} isSeat={2,-5} surface={3,-4} {4}",
                    def.defName, needs, Furniture.SeatingRule.IsSeat(def), def.surfaceType,
                    verdict));
            }

            var seat = Furniture.SeatingRule.CheapestSeat();
            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SEATING cheapest buildable seat is {0}",
                seat != null ? seat.defName : "NONE — nothing to sit on"));

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SEATING {0} predictions — {1} as predicted, {2} not", agreed + disagreed,
                agreed, disagreed));
        }

        /// <summary>
        /// Hands the colony a small herd of grazing animals, so the pen builder can be watched.
        ///
        /// The pen only runs when there are animals to hold, and a colony almost never tames one
        /// in the days it usually survives — so the code has never executed in a real run. That
        /// is how it kept a bug that stopped it placing a single fence section: nothing ever
        /// reached the line.
        ///
        /// Roamers specifically. RimWorld only asks for a pen for animals that wander off
        /// (<c>RaceProps.Roamer</c>) — a husky needs no fence and would prove nothing here.
        /// </summary>
        static void SpawnLivestock(Map map, int count)
        {
            string[] preferred = { "Cow", "Muffalo", "Alpaca", "Sheep", "Goat", "Bison", "Yak" };

            PawnKindDef kind = null;
            for (int i = 0; i < preferred.Length && kind == null; i++)
            {
                var k = DefDatabase<PawnKindDef>.GetNamedSilentFail(preferred[i]);
                if (k != null && k.RaceProps != null && k.RaceProps.Roamer) kind = k;
            }
            if (kind == null)
            {
                Chronicle.Record(ChronicleCategory.System,
                    "SCENARIO: no roaming livestock kind found — nothing that needs a pen");
                return;
            }

            var origin = HarnessSetup.ColonistOrigin(map);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                // Faction.OfPlayer is what "tamed" means to the game — the animal is colony
                // property from the moment it spawns, and the director counts it immediately.
                var animal = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                IntVec3 spot;
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(origin, map, out spot, 14))
                    spot = origin;
                GenSpawn.Spawn(animal, spot, map);
                spawned++;
            }

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: {0} tamed {1} near {2} — animals that wander, so the colony now needs " +
                "a pen it did not need before", spawned, kind.label, origin));
        }

        /// <summary>
        /// Gives the colony everything it would otherwise spend a week getting.
        ///
        /// Waiting for a colony to reach a research room the honest way costs three to eight
        /// in-game days and usually ends with the colony dead first — four runs in a row never
        /// answered the question, which is a slow way to test a placement loop that fails in one
        /// pass. Food and material are what the plan spends those days acquiring, so handing
        /// both over skips the wait without touching the behaviour under test: the planner still
        /// sites, walls and furnishes the room entirely on its own.
        /// </summary>
        static void Provision(Map map)
        {
            var origin = HarnessSetup.ColonistOrigin(map);

            int meals = HarnessSetup.StockpileFood(map, 12);
            int wood = HarnessSetup.Scatter(map, origin, "WoodLog", 2000);
            int steel = HarnessSetup.Scatter(map, origin, "Steel", 1500);
            int components = HarnessSetup.Scatter(map, origin, "ComponentIndustrial", 40);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO provision: {0} meal stacks, {1} wood, {2} steel, {3} components — the " +
                "colony now wants for nothing it would have spent a week acquiring",
                meals, wood, steel, components));
        }

        /// <summary>
        /// Removes every reason the colony has to be doing something other than research.
        ///
        /// The planner's order is fixed and deliberately so — Storage, then bedrooms, then a
        /// kitchen, and only then is Research one of the discretionary rooms the bandit may
        /// pick. None of that is shortcut here, because the thing under test is exactly that
        /// path: the planner siting a research room, walling it, and getting a three-by-two
        /// bench into it. What is removed is the *scarcity* that made those first three rooms
        /// take a week and killed four colonies before they finished.
        ///
        /// So: a stocked freezer that keeps eight days of food indefinitely, beds so nobody is
        /// sleeping on the ground and the shelter goal is not pre-empting everything, and more
        /// material than the colony can spend. Every decision after that is the director's.
        /// </summary>
        static void ClearTheWayToResearch(Map map)
        {
            HarnessSetup.ForgetRects();
            Provision(map);

            string freezer = HarnessSetup.BuildStockedFreezer(map);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO freezer: " + freezer);

            // Beds standing, in a proper room. Sleeping on the ground is a standing mood
            // complaint and an unmet shelter goal, and both pre-empt the long-term plan — which
            // is the horizon research lives on.
            var plan = HarnessSetup.Plan("Quarters", "Barracks", 11, 9,
                                         new[] { "Bed", "Bed", "Bed", "Bed", "TorchLamp" });
            string report;
            CellRect interior;
            HarnessSetup.BuildRoom(map, HarnessSetup.ColonistOrigin(map), plan, 10, 40,
                                   out interior, out report);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO quarters: " + report);

            Chronicle.Record(ChronicleCategory.System,
                "SCENARIO research: the colony is fed, housed and supplied — siting a research " +
                "room, walling it and getting the bench in is entirely the director's from here");
        }

        /// <summary>
        /// Builds one of every room the director knows about and asks the game what each one is.
        ///
        /// Two things at once. It settles the food question properly — a walled, roofed, powered
        /// freezer stocked with meals, rather than loose stacks that read as 22.5 days at dawn
        /// and 4.6 by day three — so the plan can reach its long-term horizon in an hour instead
        /// of a week. And it states, in advance and out loud, what each room is expected to
        /// classify as, so the game can disagree.
        ///
        /// The expectations are the test. RimWorld decides a room's role inside fifteen
        /// `RoomRoleWorker` classes that cannot be read from here, so an understanding of them is
        /// only worth anything if it is written down before the answer arrives.
        /// </summary>
        static void Showcase(Map map)
        {
            // Cleared before the freezer, not after. Calling it afterwards threw away the one
            // reservation that already existed, so a showcase room was sited straight on top of
            // the freezer and its ClearCell took the cooler out of the wall.
            HarnessSetup.ForgetRects();

            string freezer = HarnessSetup.BuildStockedFreezer(map);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO freezer: " + freezer);

            var origin = HarnessSetup.ColonistOrigin(map);
            var plans = HarnessSetup.Showcase();
            showcaseCentres.Clear();

            // Packed in around the colonists rather than flung across the map.
            //
            // The first version marched outward from thirty cells and added four a room, which
            // put the far ones sixty and seventy cells out — across a river, up a mountain, and
            // nowhere anybody would walk to. A colony builds its rooms next to each other, so a
            // scenario pretending to be one should too. The search still runs outward from here
            // and the overlap check keeps them apart, so this is a floor rather than a ring.
            const int NearestRoom = 12;
            for (int i = 0; i < plans.Count; i++)
            {
                string report;
                CellRect interior;
                HarnessSetup.BuildRoom(map, origin, plans[i], NearestRoom, 70,
                                       out interior, out report);
                showcaseCentres.Add(interior);
                Chronicle.Record(ChronicleCategory.System, "SCENARIO room: " + report);
            }

            showcaseAt = Find.TickManager.TicksGame + 900;
        }

        /// <summary>
        /// Reads the verdicts back once the game has had time to recalculate the rooms.
        ///
        /// Room stats are cached and updated on a priority queue rather than on demand, so
        /// asking the instant the last wall goes up returns the roomless defaults for most of
        /// them — which reads as every room having failed.
        /// </summary>
        static void ReportShowcase(Map map)
        {
            var plans = HarnessSetup.Showcase();

            Chronicle.Record(ChronicleCategory.System,
                "SCENARIO showcase — what the game calls each room it was handed:");

            for (int i = 0; i < plans.Count; i++)
            {
                var found = i < showcaseCentres.Count ? showcaseCentres[i] : default(CellRect);
                if (found.Area <= 0)
                {
                    Chronicle.Record(ChronicleCategory.System,
                        "SCENARIO   " + plans[i].label + " — never got built");
                    continue;
                }
                Chronicle.Record(ChronicleCategory.System, "SCENARIO   " + plans[i].label + " -> " +
                    HarnessSetup.Verdict(map, found, plans[i].expectedRole));
            }
        }

        static int showcaseAt = -1;
        static readonly List<CellRect> showcaseCentres = new List<CellRect>();

        static void FireRaid(Map map, float points)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.points = points;
            parms.forced = true;

            bool ok = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: raid at {0:0} points — {1}", points, ok ? "fired" : "REFUSED"));
        }

        /// <summary>
        /// Drops someone on the map already downed, which is the state both capture and rescue
        /// key off. Hostility decides which of the two is even available.
        /// </summary>
        static void SpawnDownedStranger(Map map, bool hostile)
        {
            // Both routes need somewhere to put the person, and a day-nought colony has no beds
            // at all — without this the scenario only ever proves that. The bed is setup; what
            // the director does about the body is the part under test.
            SpawnBed(map, forPrisoners: hostile);

            // The colony must be fed for either route to be on the table at all: a starving one
            // is in a standing emergency, and neither capturing nor rescuing is something to do
            // while there is nothing to eat. Setup, not the behaviour under test.
            HarnessSetup.StockpileFood(map);

            var faction = hostile
                ? Find.FactionManager.RandomEnemyFaction(false, false, true)
                : Find.FactionManager.RandomNonHostileFaction(false, false, true);

            // A world can be generated with no faction of the kind asked for, and a null here
            // took the whole scenario down with a null reference.
            // Any settled neighbour will do for a non-hostile stranger; some worlds have no
            // faction of the exact kind asked for.
            if (faction == null && !hostile)
            {
                var all = Find.FactionManager.AllFactionsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] == null || all[i].IsPlayer) continue;
                    if (all[i].HostileTo(Faction.OfPlayer)) continue;
                    faction = all[i];
                    break;
                }
            }

            if (faction == null)
            {
                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SCENARIO: no {0} faction in this world", hostile ? "enemy" : "neutral"));
                return;
            }

            var kind = PawnKindDefOf.Villager;
            var pawn = PawnGenerator.GeneratePawn(kind, faction);

            var origin = HarnessSetup.ColonistOrigin(map);

            IntVec3 spot;
            if (!CellFinder.TryFindRandomSpawnCellForPawnNear(origin, map, out spot, 12))
                spot = origin;

            GenSpawn.Spawn(pawn, spot, map);

            // Anaesthetised rather than beaten unconscious. Damaging them until they dropped had
            // them bleed out within a few in-game hours — before the director's next pass — so
            // the scenario only ever proved that a corpse is not a prisoner.
            var anesthetic = DefDatabase<HediffDef>.GetNamedSilentFail("Anesthetic");
            if (anesthetic != null) pawn.health.AddHediff(anesthetic);
            else HealthUtility.DamageUntilDowned(pawn, false);

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: dropped {0} {1} at {2}, downed",
                hostile ? "hostile" : "neutral", pawn.LabelShortCap, spot));
        }

        /// <summary>
        /// Throws a 3x3 walled cell with a door around a spot, so a prisoner bed inside it is in
        /// a room the game will accept. Returns false if the ground will not take it.
        /// </summary>
        /// <summary>
        /// Stands a bed near the colonists, inside a walled cell when it is for prisoners.
        ///
        /// The cell is not decoration: the game will not carry anyone to a prisoner bed that is
        /// not in a room enclosing it, which is exactly why the planner has to build a Prison
        /// *room* rather than drop a bed somewhere.
        /// </summary>
        static void SpawnBed(Map map, bool forPrisoners)
        {
            var def = AcDefs.Bed;
            if (def == null) return;

            var origin = HarnessSetup.ColonistOrigin(map);

            foreach (var cell in GenRadial.RadialCellsAround(origin, 14, true))
            {
                if (!GenSpawn.CanSpawnAt(def, cell, map, Rot4.North)) continue;
                if (forPrisoners && !BuildCellAround(map, cell)) continue;

                var bed = HarnessSetup.PlaceFinished(map, def, cell, 0, 0) as Building_Bed;
                if (bed == null) continue;

                if (forPrisoners) spawnedPrisonBed = bed;

                var room = bed.GetRoom();
                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SCENARIO: placed a {0} bed at {1} — room={2}, cells={3}, outdoors={4}",
                    forPrisoners ? "prisoner" : "colonist", cell,
                    room != null ? "yes" : "NONE",
                    room != null ? room.CellCount : 0,
                    room != null && room.PsychologicallyOutdoors));
                return;
            }
        }

        static bool BuildCellAround(Map map, IntVec3 centre)
        {
            var wall = AcDefs.Wall;
            var door = AcDefs.Door;
            if (wall == null || door == null) return false;

            var rect = CellRect.CenteredOn(centre, 1).ExpandedBy(1);
            if (!rect.InBounds(map)) return false;

            foreach (var cell in rect)
            {
                if (!cell.InBounds(map)) return false;
                if (cell != centre && cell.GetEdifice(map) != null) return false;
            }

            var stuff = GenStuff.DefaultStuffFor(wall);
            var doorCell = new IntVec3(rect.minX + rect.Width / 2, 0, rect.minZ);

            foreach (var cell in rect.EdgeCells)
            {
                var def = cell == doorCell ? door : wall;
                var built = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                built.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(built, cell, map, Rot4.North);
            }

            foreach (var cell in rect) map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

            // The walls went up this instant, so the room they enclose does not exist yet.
            // Marking a bed for prisoners before the rebuild asks about a room that is still the
            // great outdoors, and the answer is no.
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            return true;
        }

        static void FireIncident(Map map, string defName, float points)
        {
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Chronicle.Record(ChronicleCategory.System, "SCENARIO: no incident " + defName);
                return;
            }

            var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.points = points;
            parms.forced = true;

            bool ok = def.Worker.TryExecute(parms);
            Chronicle.Record(ChronicleCategory.System,
                "SCENARIO: " + defName + " — " + (ok ? "fired" : "REFUSED"));
        }

        static void FireCondition(Map map, string defName)
        {
            var def = DefDatabase<GameConditionDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                Chronicle.Record(ChronicleCategory.System, "SCENARIO: no condition " + defName);
                return;
            }

            var condition = GameConditionMaker.MakeCondition(def, 120000);
            map.gameConditionManager.RegisterCondition(condition);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO: " + defName + " started");
        }

        /// <summary>Fires against the base itself, which is the case the director must answer.</summary>
        static void StartFires(Map map)
        {
            var origin = HarnessSetup.ColonistOrigin(map);

            int lit = 0;
            foreach (var cell in GenRadial.RadialCellsAround(origin, 8, true))
            {
                if (lit >= 6) break;
                if (!cell.InBounds(map) || cell.GetFirstThing<Fire>(map) != null) continue;
                if (!GenGrid.Standable(cell, map)) continue;

                FireUtility.TryStartFireIn(cell, map, 0.5f, null);
                lit++;
            }
            Chronicle.Record(ChronicleCategory.System, "SCENARIO: lit " + lit + " fires at the colony");
        }

        /// <summary>
        /// Leaves a colonist's corpse on the ground — the state that cost a real colony a
        /// standing -10 mood penalty for eleven days because nothing ever buried anyone.
        /// </summary>
        /// <summary>
        /// Puts a colonist on the floor, alive, far from anyone, and leaves them there.
        ///
        /// The gap this tests killed run 38: every one of its four deaths was a pawn who was
        /// downed and then died rather than being killed outright, with five days of food in
        /// the larder they could not walk to. Evacuation only ran when something was burning,
        /// so nothing ever carried them anywhere. The backstop that now answers that has never
        /// been seen work, because a colony has to have a casualty first and the `downed`
        /// scenario spawns a downed *stranger*, which is a different code path entirely.
        ///
        /// Damaged rather than hediff-ed, because being downed by injury is the state the
        /// colony actually meets. Kept off the last colonist: a colony of one that goes down is
        /// lost by definition and proves nothing about rescuing.
        /// </summary>
        static void DownAColonist(Map map)
        {
            var colonists = map.mapPawns.FreeColonists;
            if (colonists.Count <= 2)
            {
                Chronicle.Record(ChronicleCategory.System,
                    "SCENARIO casualty: too few colonists — somebody has to be left standing to " +
                    "do the carrying, or this tests nothing");
                return;
            }

            var victim = colonists[colonists.Count - 1];
            string name = victim.LabelShortCap;

            // Beaten down rather than shot: blunt damage to a limb drops a pawn without the
            // bleeding that would make this a test of whether they die before anyone arrives.
            int guard = 0;
            while (!victim.Downed && !victim.Dead && guard++ < 40)
            {
                BodyPartRecord part = null;
                foreach (var candidate in victim.health.hediffSet.GetNotMissingParts())
                {
                    if (candidate != null && candidate.def == BodyPartDefOf.Leg) { part = candidate; break; }
                }
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 8f, 0f, -1f, null, part));
            }

            if (!victim.Downed)
            {
                Chronicle.Record(ChronicleCategory.System,
                    "SCENARIO casualty: could not put " + name + " down in 40 blows");
                return;
            }

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO casualty: {0} is down at {1} and nothing is burning — whether anybody " +
                "comes for them is the director's, and an hour on the floor is the point at " +
                "which it should stop waiting",
                name, victim.Position));
        }

        static void KillAColonist(Map map)
        {
            var colonists = map.mapPawns.FreeColonists;
            if (colonists.Count <= 1)
            {
                Chronicle.Record(ChronicleCategory.System, "SCENARIO: too few colonists to spare one");
                return;
            }

            var victim = colonists[colonists.Count - 1];
            string name = victim.LabelShortCap;
            victim.Kill(null, null);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO: killed " + name + ", corpse left where it fell");
        }

        static void RemoveAllFood(Map map)
        {
            int removed = 0;
            var things = new List<Thing>(map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree));
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing.def.category != ThingCategory.Item) continue;

                removed += thing.stackCount;
                thing.Destroy(DestroyMode.Vanish);
            }
            Chronicle.Record(ChronicleCategory.System, "SCENARIO: removed " + removed + " food");
        }

        static Building_Bed spawnedPrisonBed;

        /// <summary>
        /// Marks the bed now that the room around it exists, and says whether it took.
        ///
        /// Both halves are needed: the flag on the bed, and a nudge to the room, because
        /// `IsPrisonCell` is cached there rather than derived on demand. Without the nudge the
        /// game refuses with "no enclosed prisoner-marked bed" while every clause of that
        /// sentence looks satisfied from outside.
        /// </summary>
        void MarkPendingPrisonBed()
        {
            var bed = pendingPrisonBed;
            pendingPrisonBed = null;
            spawnedPrisonBed = null;
            if (bed == null || !bed.Spawned) return;

            HarnessSetup.MarkAsPrisonBed(bed);
            var room = bed.GetRoom();

            Chronicle.Record(ChronicleCategory.System, string.Format(
                "SCENARIO: marked the prisoner bed once its room settled — ForPrisoners={0}, " +
                "roomCanBePrison={1}, prisonCell={2}",
                bed.ForPrisoners,
                room != null && Building_Bed.RoomCanBePrisonCell(room),
                room != null && room.IsPrisonCell));
        }

        // ---------------------------------------------------------------- what actually happens

        /// <summary>
        /// Names what each colonist is doing, not merely that they were drafted.
        ///
        /// Drafting and fighting are different things, and the gap between them is invisible in
        /// any summary that only counts how many were mobilised.
        /// </summary>
        static void Report(Map map)
        {
            var line = new System.Text.StringBuilder("SCENARIO: ");

            int hostiles = 0, downedHostiles = 0;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if (p.Downed) downedHostiles++; else hostiles++;
            }

            line.Append(hostiles).Append(" hostiles up, ").Append(downedHostiles).Append(" down | ");

            var colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                var p = colonists[i];
                if (p == null || p.Dead) continue;

                line.Append(p.LabelShortCap).Append(p.Drafted ? "[drafted]" : "[free]");
                line.Append(p.Downed ? "(DOWN)" : "");
                line.Append('=').Append(p.CurJobDef != null ? p.CurJobDef.defName : "idle");
                line.Append("  ");
            }

            var report = line.ToString();
            if (report == lastReportLine) return;
            lastReportLine = report;
            Chronicle.Record(ChronicleCategory.System, report);
        }

        static string lastReportLine;
    }
}
