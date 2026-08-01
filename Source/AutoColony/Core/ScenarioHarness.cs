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
                else if (scenario == "strip") StripMaterials(map);
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
            FeedColony(map);

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

            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

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

        /// <summary>Stands a finished bed near the colonists, prisoner-marked if asked.</summary>
        static void SpawnBed(Map map, bool forPrisoners)
        {
            var def = AcDefs.Bed;
            if (def == null) return;

            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            foreach (var cell in GenRadial.RadialCellsAround(origin, 14, true))
            {
                if (!GenSpawn.CanSpawnAt(def, cell, map, Rot4.North)) continue;

                // A prisoner bed standing in the open is not a prison. The game will not let
                // anyone be carried to one unless it sits in a room that actually encloses it,
                // so the walls are part of the setup — which is exactly why the planner has to
                // build a Prison *room* rather than just drop a bed somewhere.
                if (forPrisoners && !BuildCellAround(map, cell)) continue;

                var bed = (Building_Bed)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));

                // Owned *before* it spawns. SpawnSetup is what registers a building with its
                // room and with the faction-dependent systems around it, so a bed that spawns
                // ownerless registers as nobody's and setting the faction afterwards never
                // revisits that — which is why a marked bed in an eligible room still would not
                // make the room a prison.
                bed.SetFactionDirect(Faction.OfPlayer);
                if (forPrisoners) bed.ForOwnerType = BedOwnerType.Prisoner;

                GenSpawn.Spawn(bed, cell, map, Rot4.North);

                // Rebuild *after* the bed is in. Spawning it dirties the regions again, and
                // marking a bed while the room around it is stale asks the question of the wrong
                // room — which is how a walled cell with a door still failed to be a prison.
                map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

                // Marked later, not now. The walls went up this instant, and a room cannot settle
                // in the tick it is created — the game rebuilds regions on its own schedule, and
                // anything asked before that gets an answer about the room that was there before.
                // The real planner never hits this because colonists take hours to build a wall.
                if (forPrisoners) spawnedPrisonBed = bed;

                var room = bed.GetRoom();
                Chronicle.Record(ChronicleCategory.System, string.Format(
                    "SCENARIO: placed a {0} bed at {1} — ForPrisoners={2}, room={3}, cells={4}, " +
                    "outdoors={5}, prisonCell={6}",
                    forPrisoners ? "prisoner" : "colonist", cell, bed.ForPrisoners,
                    room != null ? "yes" : "NONE",
                    room != null ? room.CellCount : 0,
                    room != null && room.PsychologicallyOutdoors,
                    room != null && room.IsPrisonCell));
                return;
            }
        }

        /// <summary>
        /// Throws a 3x3 walled cell with a door around a spot, so a prisoner bed inside it is in
        /// a room the game will accept. Returns false if the ground will not take it.
        /// </summary>
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

        /// <summary>
        /// Puts food where the colony can count it.
        ///
        /// <c>daysOfFood</c> comes off <c>ResourceCounter</c>, which sees only what is in a
        /// stockpile — so meals dropped on the ground read as nothing and the colony stays in a
        /// food emergency however much is lying about. The zone has to exist first.
        /// </summary>
        static void FeedColony(Map map)
        {
            var meal = AcDefs.Thing("MealSurvivalPack");
            if (meal == null || map.zoneManager == null) return;

            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);

            int filled = 0;
            foreach (var cell in GenRadial.RadialCellsAround(origin, 8, true))
            {
                if (filled >= 6) break;
                if (!cell.InBounds(map) || !GenGrid.Standable(cell, map)) continue;
                if (cell.GetFirstItem(map) != null) continue;
                if (map.zoneManager.ZoneAt(cell) != null) continue;

                zone.AddCell(cell);

                var stack = ThingMaker.MakeThing(meal, null);
                stack.stackCount = meal.stackLimit;
                GenSpawn.Spawn(stack, cell, map);
                stack.SetForbidden(false, false);
                filled++;
            }

            Chronicle.Record(ChronicleCategory.System,
                "SCENARIO: stockpiled " + filled + " stacks of meals");
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
            var origin = map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;

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

        /// <summary>Takes the building materials away, to see what a destitute colony does.</summary>
        static void StripMaterials(Map map)
        {
            int removed = 0;
            var names = new List<string> { "WoodLog", "Steel" };
            names.AddRange(AcDefs.StoneBlockStuff);

            for (int i = 0; i < names.Count; i++)
            {
                var def = AcDefs.Thing(names[i]);
                if (def == null) continue;

                var stacks = new List<Thing>(map.listerThings.ThingsOfDef(def));
                for (int s = 0; s < stacks.Count; s++)
                {
                    if (stacks[s] == null || !stacks[s].Spawned) continue;
                    removed += stacks[s].stackCount;
                    stacks[s].Destroy(DestroyMode.Vanish);
                }
            }
            Chronicle.Record(ChronicleCategory.System, "SCENARIO: removed " + removed + " building material");
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

            bed.ForOwnerType = BedOwnerType.Prisoner;

            var room = bed.GetRoom();
            if (room != null)
            {
                // Both notifications. Notify_BedTypeChanged alone left IsPrisonCell false even
                // with the bed marked and the room eligible; what actually made it take, when
                // done by hand in game, was reinstalling the bed — which is a despawn and a
                // respawn, and that is the notification respawning sends.
                room.Notify_BedTypeChanged();
                room.Notify_ContainedThingSpawnedOrDespawned(bed);
            }

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
