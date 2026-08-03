using System;
using System.Collections.Generic;
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
                else if (scenario == "manhunters") FireIncident(map, "ManhunterPack", 400f);
                else if (scenario == "infestation") FireIncident(map, "Infestation", 500f);
                else if (scenario == "starve") RemoveAllFood(map);
                else if (scenario == "provision") Provision(map);
                else if (scenario == "showcase") { Provision(map); Showcase(map); }
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
            string freezer = HarnessSetup.BuildStockedFreezer(map);
            Chronicle.Record(ChronicleCategory.System, "SCENARIO freezer: " + freezer);

            var origin = HarnessSetup.ColonistOrigin(map);
            var plans = HarnessSetup.Showcase();
            var centres = new List<IntVec3>();

            // Marched outward so the rooms do not sit on one another, and started well clear of
            // the colonists so the planner still has somewhere of its own to build.
            int distance = 30;
            for (int i = 0; i < plans.Count; i++)
            {
                string built = HarnessSetup.BuildRoom(map, origin, plans[i], distance, distance + 22);
                Chronicle.Record(ChronicleCategory.System, "SCENARIO room: " + built);
                distance += 4;
            }

            showcaseAt = Find.TickManager.TicksGame + 600;
            showcaseOrigin = origin;
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
            var origin = showcaseOrigin;

            Chronicle.Record(ChronicleCategory.System,
                "SCENARIO showcase — what the game calls each room it was handed:");

            for (int i = 0; i < plans.Count; i++)
            {
                var found = FindRoomCentre(map, origin, plans[i]);
                if (!found.IsValid)
                {
                    Chronicle.Record(ChronicleCategory.System,
                        "SCENARIO   " + plans[i].label + " — never got built");
                    continue;
                }
                Chronicle.Record(ChronicleCategory.System, "SCENARIO   " + plans[i].label + " -> " +
                    HarnessSetup.Verdict(map, found, plans[i].expectedRole));
            }
        }

        /// <summary>
        /// Finds a built showcase room by looking for an enclosed space of about the right size.
        ///
        /// The build returns its centre, but keeping those across a tick boundary means holding
        /// map state the scenario has no business owning — so they are found again instead.
        /// </summary>
        static IntVec3 FindRoomCentre(Map map, IntVec3 origin, HarnessSetup.RoomPlan plan)
        {
            int wantCells = (plan.width - 2) * (plan.height - 2);

            foreach (var cell in GenRadial.RadialCellsAround(origin, 60, true))
            {
                if (!cell.InBounds(map)) continue;
                var room = cell.GetRoom(map);
                if (room == null || room.TouchesMapEdge || room.PsychologicallyOutdoors) continue;
                if (room.CellCount != wantCells) continue;
                if (seenShowcaseRooms.Contains(room.ID)) continue;

                seenShowcaseRooms.Add(room.ID);
                return cell;
            }
            return IntVec3.Invalid;
        }

        static int showcaseAt = -1;
        static IntVec3 showcaseOrigin;
        static readonly HashSet<int> seenShowcaseRooms = new HashSet<int>();

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
