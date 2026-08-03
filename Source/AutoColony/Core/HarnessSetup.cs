using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// The bits of stage-setting both test harnesses need.
    ///
    /// They had grown their own copies of all of it — spawning a bed, getting food somewhere the
    /// colony would count it, dropping materials, standing a finished building — and the copies
    /// had already drifted: one spawned a bed and then set its faction, the other had learned the
    /// hard way that the order matters and did it the other way round. Sharing them is what stops
    /// a lesson landing in one harness and not the other.
    ///
    /// None of this is the thing under test. It exists so a scenario can start from a colony
    /// that is fed, housed and equipped, and leave the director's own behaviour as the only
    /// variable.
    /// </summary>
    public static class HarnessSetup
    {
        /// <summary>Where the colonists are, or the middle of the map before there are any.</summary>
        public static IntVec3 ColonistOrigin(Map map)
        {
            return map.mapPawns.FreeColonists.Count > 0
                ? map.mapPawns.FreeColonists[0].Position
                : map.Center;
        }

        /// <summary>
        /// Stands a finished, player-owned building on the first spot that will take it.
        ///
        /// Faction goes on *before* the spawn. `SpawnSetup` is what registers a building with its
        /// room, so one spawned ownerless registers as nobody's — and setting the faction after
        /// never revisits that. A prisoner bed built the wrong way round is marked, enclosed,
        /// eligible, and still refused by the game.
        /// </summary>
        public static Thing PlaceFinished(Map map, ThingDef def, IntVec3 origin,
                                          int minDist = 0, int maxDist = 14, Rot4? rot = null)
        {
            if (def == null) return null;
            var rotation = rot ?? Rot4.North;

            foreach (var cell in GenRadial.RadialCellsAround(origin, maxDist, true))
            {
                if ((cell - origin).LengthHorizontal < minDist) continue;
                if (!GenSpawn.CanSpawnAt(def, cell, map, rotation)) continue;

                var thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                thing.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(thing, cell, map, rotation);
                return thing;
            }
            return null;
        }

        /// <summary>Beds the colony owns, so somebody has somewhere to be carried to.</summary>
        public static int SpawnBeds(Map map, int count, bool forPrisoners = false)
        {
            var def = AcDefs.Bed;
            if (def == null) return 0;

            var origin = ColonistOrigin(map);
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                var bed = PlaceFinished(map, def, origin) as Building_Bed;
                if (bed == null) break;
                if (forPrisoners) MarkAsPrisonBed(bed);
                placed++;
            }
            return placed;
        }

        /// <summary>
        /// Makes a bed a prisoner bed, and tells its room so.
        ///
        /// `IsPrisonCell` is cached on the room rather than derived, so the flag alone leaves the
        /// game refusing a capture with "no enclosed prisoner-marked bed" while every clause of
        /// that sentence looks satisfied.
        /// </summary>
        public static void MarkAsPrisonBed(Building_Bed bed)
        {
            if (bed == null) return;
            bed.ForOwnerType = BedOwnerType.Prisoner;

            var room = bed.GetRoom();
            if (room == null) return;

            room.Notify_BedTypeChanged();
            room.Notify_ContainedThingSpawnedOrDespawned(bed);
        }

        /// <summary>
        /// Puts food where the colony can count it.
        ///
        /// `daysOfFood` comes off `ResourceCounter`, which sees only stockpiled goods — so meals
        /// dropped on the ground read as nothing and the colony stays in a food emergency however
        /// much is lying about. The zone has to exist first.
        /// </summary>
        public static int StockpileFood(Map map, int stacks = 6)
        {
            var meal = AcDefs.Thing("MealSurvivalPack");
            if (meal == null || map.zoneManager == null) return 0;

            var origin = ColonistOrigin(map);
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);

            int filled = 0;
            foreach (var cell in GenRadial.RadialCellsAround(origin, 8, true))
            {
                if (filled >= stacks) break;
                if (!cell.InBounds(map) || !GenGrid.Standable(cell, map)) continue;
                if (cell.GetFirstItem(map) != null) continue;
                if (map.zoneManager.ZoneAt(cell) != null) continue;

                zone.AddCell(cell);
                Drop(map, cell, meal, meal.stackLimit);
                filled++;
            }
            return filled;
        }

        /// <summary>Scatters loose, unforbidden material around a spot.</summary>
        public static int Scatter(Map map, IntVec3 near, string defName, int count)
        {
            var def = AcDefs.Thing(defName);
            if (def == null) return 0;

            int perStack = def.stackLimit > 0 ? def.stackLimit : 75;
            int remaining = count;

            foreach (var cell in GenRadial.RadialCellsAround(near, 12, true))
            {
                if (remaining <= 0) break;
                if (!cell.InBounds(map) || !cell.Standable(map)) continue;
                if (cell.GetFirstItem(map) != null) continue;

                remaining -= Drop(map, cell, def, remaining < perStack ? remaining : perStack);
            }
            return count - remaining;
        }

        static int Drop(Map map, IntVec3 cell, ThingDef def, int amount)
        {
            var stack = ThingMaker.MakeThing(def, null);
            stack.stackCount = amount;
            GenSpawn.Spawn(stack, cell, map);
            stack.SetForbidden(false, false);
            return amount;
        }

        /// <summary>
        /// Stands a finished, powered, stocked freezer on the map and returns what it holds.
        ///
        /// Loose meals in a bare stockpile do not settle the food question. Watched in run 39:
        /// twelve stacks read as 22.5 days at dawn and 4.6 days by day three, the plan was still
        /// on "Stock food, 1.9 of 8 days" with seventeen hunts standing, and the colony spent its
        /// first two days hunting instead of building. Food that is merely *present* is not food
        /// the colony counts as secure — the goal wants eight days of it, kept.
        ///
        /// So this builds the thing a colony would build: walls, a roof, a cooler in the wall,
        /// and a fuelled generator wired to it. Simple meals rot in a day and a half at room
        /// temperature and effectively never in a freezer, which is the whole difference between
        /// a scenario that holds and one that drains while you watch.
        ///
        /// Setup, not behaviour: nothing here is the director's job, and the director is free to
        /// ignore all of it. It exists so the plan can reach its long-term horizon in an hour
        /// rather than a week.
        /// </summary>
        public static string BuildStockedFreezer(Map map, int mealCount = 240)
        {
            var wall = AcDefs.Wall;
            var meal = AcDefs.Thing("MealSimple");
            if (wall == null || meal == null) return "no wall or meal def";

            var origin = ColonistOrigin(map);

            // Far enough out that it does not sit on the spot the planner wants for its own
            // rooms, near enough that hauling to it is not a day's walk.
            CellRect rect;
            if (!FindClearRect(map, origin, 9, 9, 14, 26, out rect)) return "nowhere clear to put it";

            var stuff = GenStuff.DefaultStuffFor(wall);
            int walls = 0;
            foreach (var cell in rect.EdgeCells)
            {
                ClearCell(map, cell);
                var w = ThingMaker.MakeThing(wall, stuff);
                w.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(w, cell, map);
                walls++;
            }

            // A door, so the colony can actually get in. Without one the room is sealed and
            // every haul job to it is unreachable — which reads exactly like a director bug.
            var doorCell = new IntVec3(rect.minX + rect.Width / 2, 0, rect.minZ);
            ClearCell(map, doorCell);
            var door = ThingMaker.MakeThing(AcDefs.Door, GenStuff.DefaultStuffFor(AcDefs.Door));
            door.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(door, doorCell, map, Rot4.North);

            var interior = rect.ContractedBy(1);
            foreach (var cell in interior)
            {
                ClearCell(map, cell);
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }

            // The cooler spans the wall by design — inside face cold, outside face hot — so it
            // replaces a wall cell rather than standing in the room.
            string power = InstallCooling(map, rect);

            // Filled last, once there is somewhere for it to be counted. The zone is what makes
            // ResourceCounter see any of it.
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);

            int placed = 0;
            foreach (var cell in interior)
            {
                if (placed >= mealCount) break;
                if (!cell.Standable(map) || cell.GetFirstItem(map) != null) continue;
                if (map.zoneManager.ZoneAt(cell) != null) continue;

                zone.AddCell(cell);
                int amount = meal.stackLimit;
                if (placed + amount > mealCount) amount = mealCount - placed;
                Drop(map, cell, meal, amount);
                placed += amount;
            }

            return string.Format("{0} walls at {1}, {2} meals stocked, {3}",
                                 walls, rect.CenterCell, placed, power);
        }

        /// <summary>
        /// Puts a cooler in the freezer wall and a fuelled generator on a conduit that reaches it.
        ///
        /// A cooler with no power is a hole in the wall, and a generator with no fuel is
        /// furniture — both were worth stating separately, because either alone leaves the room
        /// at outdoor temperature and the meals rotting on schedule.
        /// </summary>
        static string InstallCooling(Map map, CellRect rect)
        {
            var cooler = AcDefs.Cooler;
            var generator = AcDefs.WoodFiredGenerator;
            var conduit = AcDefs.PowerConduit;
            if (cooler == null || generator == null || conduit == null) return "no power defs";

            // North wall, facing out: the cold side is the side the cooler's back is to.
            var coolerCell = new IntVec3(rect.minX + rect.Width / 2, 0, rect.maxZ);
            ClearCell(map, coolerCell);
            var c = ThingMaker.MakeThing(cooler, GenStuff.DefaultStuffFor(cooler));
            c.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(c, coolerCell, map, Rot4.North);

            // Conduit from the cooler out to where the generator will stand, so the two share a
            // power net. Generators are placed clear of the room because they burn.
            var genCell = new IntVec3(coolerCell.x, 0, coolerCell.z + 4);
            for (int z = coolerCell.z; z <= genCell.z + 1; z++)
            {
                var cell = new IntVec3(coolerCell.x, 0, z);
                if (!cell.InBounds(map)) continue;
                if (cell == coolerCell) continue;
                ClearCell(map, cell);
                var line = ThingMaker.MakeThing(conduit, null);
                line.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(line, cell, map);
            }

            ClearCell(map, genCell);
            var g = ThingMaker.MakeThing(generator, GenStuff.DefaultStuffFor(generator));
            g.SetFactionDirect(Faction.OfPlayer);
            var spawned = GenSpawn.Spawn(g, genCell, map, Rot4.North);

            // Fuelled to the top. An empty generator is the same as no generator, and the
            // colony refuelling it is not what this scenario is testing.
            var refuelable = spawned.TryGetComp<CompRefuelable>();
            if (refuelable != null) refuelable.Refuel(refuelable.Props.fuelCapacity);

            return refuelable != null
                ? "cooler powered by a full generator"
                : "cooler powered, generator takes no fuel";
        }

        /// <summary>A rectangle with nothing standing in it, searched outward from a point.</summary>
        static bool FindClearRect(Map map, IntVec3 origin, int width, int height,
                                  int minDist, int maxDist, out CellRect rect)
        {
            rect = default(CellRect);
            foreach (var centre in GenRadial.RadialCellsAround(origin, maxDist, true))
            {
                if ((centre - origin).LengthHorizontal < minDist) continue;

                var candidate = CellRect.CenteredOn(centre, width / 2, height / 2);
                if (!candidate.InBounds(map)) continue;

                bool clear = true;
                foreach (var cell in candidate)
                {
                    if (!cell.InBounds(map) || cell.GetEdifice(map) != null ||
                        !GenGrid.Standable(cell, map)) { clear = false; break; }
                }
                if (!clear) continue;

                rect = candidate;
                return true;
            }
            return false;
        }

        /// <summary>Takes whatever is loose in a cell out of the way of what is going there.</summary>
        static void ClearCell(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return;
            var things = new List<Thing>(cell.GetThingList(map));
            for (int i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing is Pawn) continue;
                if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
            }
        }

        // ------------------------------------------------------------------ the showcase

        /// <summary>
        /// One room to stand on the map, and what the game is expected to call it.
        ///
        /// The expectation is the point. RimWorld decides a room's role from what is standing in
        /// it, by rules living in fifteen `RoomRoleWorker` classes that are not readable from
        /// here — so the only honest way to check an understanding of them is to state it in
        /// advance and let the game disagree out loud.
        /// </summary>
        public struct RoomPlan
        {
            public string label;
            public string expectedRole;
            public int width;
            public int height;
            public string[] contents;
            public bool markMedical;
            public bool markPrison;
        }

        /// <summary>
        /// Every kind of room the director knows about, built correctly, side by side.
        ///
        /// A colony reaches one or two of these in a week and never sees most of them, so the
        /// mapping from "what the planner calls it" to "what the game calls it" has only ever
        /// been checked one room at a time, days apart, on colonies that kept dying. Standing
        /// all of them up at once turns that into a single readable table.
        /// </summary>
        public static List<RoomPlan> Showcase()
        {
            var plans = new List<RoomPlan>();

            // One bed is a bedroom. This is the good case and the reference for the next one.
            plans.Add(Plan("Bedroom", "Bedroom", 7, 7, new[] { "Bed", "TorchLamp" }));

            // Three beds in the same room is not a fuller bedroom, it is a barracks — a strictly
            // worse mood curve, -7 at the floor against -2. Built deliberately to show the pair.
            plans.Add(Plan("Barracks (3 beds)", "Barracks", 9, 7,
                           new[] { "Bed", "Bed", "Bed", "TorchLamp" }));

            // A table and chairs. Chairs matter: a table alone is furniture, the room only reads
            // as somewhere to eat once there is somewhere to sit.
            plans.Add(Plan("Dining", "DiningRoom", 9, 7,
                           new[] { "Table2x2c", "DiningChair", "DiningChair", "TorchLamp" }));

            // Joy buildings, and more than one kind — recreation is satisfied per kind, so
            // variety is what a rec room is actually for. The horseshoes pin wants a clear lane
            // to throw down, which is why this room is the widest of them.
            plans.Add(Plan("Recreation", "RecRoom", 11, 9,
                           new[] { "ChessTable", "GameOfUrBoard", "HorseshoesPin", "TorchLamp" }));

            // Medical beds, not ordinary ones. An unmarked bed in a room called a hospital is a
            // bedroom, which is exactly the bug this session found in the planner.
            plans.Add(Plan("Hospital", "Hospital", 9, 7,
                           new[] { "Bed", "Bed", "TorchLamp" }, medical: true));

            // A research bench, which is three cells by two — the footprint that would not fit
            // in a 5x5 interior and cost thirty-seven colonies their research.
            plans.Add(Plan("Research", "Laboratory", 9, 7,
                           new[] { "SimpleResearchBench", "TorchLamp" }));

            plans.Add(Plan("Workshop", "Workshop", 9, 7,
                           new[] { "TableStonecutter", "ElectricTailoringBench", "TorchLamp" }));

            plans.Add(Plan("Kitchen", "Kitchen", 9, 7,
                           new[] { "FueledStove", "TableButcher", "TorchLamp" }));

            // Shelves, not a stockpile zone. A storeroom is defined by what is stored in it, and
            // shelves are what the game counts.
            plans.Add(Plan("Storage", "Storeroom", 9, 9,
                           new[] { "Shelf", "Shelf", "Shelf", "TorchLamp" }));

            // A prisoner bed, marked and its room told. The flag alone leaves the game refusing
            // captures with "no enclosed prisoner-marked bed" while every clause looks satisfied.
            plans.Add(Plan("Prison", "PrisonCell", 7, 7,
                           new[] { "Bed", "TorchLamp" }, prison: true));

            plans.Add(Plan("Tomb", "Tomb", 7, 7, new[] { "Grave", "Grave", "TorchLamp" }));

            plans.Add(Plan("Barn", "Barn", 9, 9,
                           new[] { "AnimalSleepingSpot", "AnimalSleepingSpot", "Hopper", "TorchLamp" }));

            return plans;
        }

        static RoomPlan Plan(string label, string expected, int w, int h, string[] contents,
                             bool medical = false, bool prison = false)
        {
            var p = new RoomPlan();
            p.label = label;
            p.expectedRole = expected;
            p.width = w;
            p.height = h;
            p.contents = contents;
            p.markMedical = medical;
            p.markPrison = prison;
            return p;
        }

        /// <summary>
        /// Stands one planned room on the map and reports what the game made of it.
        ///
        /// Enclosure is the whole trick and the easiest thing to get wrong: RimWorld only rates
        /// a space that is walled all the way round *and* roofed, and a single missing cell
        /// leaves it reading as outdoors with every stat at its roomless default. The door has to
        /// be there too — a sealed box is unreachable, and every job into it fails in a way that
        /// looks like a director bug rather than a scenario one.
        /// </summary>
        public static string BuildRoom(Map map, IntVec3 near, RoomPlan plan, int minDist, int maxDist)
        {
            var wall = AcDefs.Wall;
            if (wall == null) return plan.label + ": no wall def";

            CellRect rect;
            if (!FindClearRect(map, near, plan.width, plan.height, minDist, maxDist, out rect))
                return plan.label + ": nowhere clear";

            var stuff = GenStuff.DefaultStuffFor(wall);
            foreach (var cell in rect.EdgeCells)
            {
                ClearCell(map, cell);
                var w = ThingMaker.MakeThing(wall, stuff);
                w.SetFactionDirect(Faction.OfPlayer);
                GenSpawn.Spawn(w, cell, map);
            }

            var doorCell = new IntVec3(rect.minX + rect.Width / 2, 0, rect.minZ);
            ClearCell(map, doorCell);
            var door = ThingMaker.MakeThing(AcDefs.Door, GenStuff.DefaultStuffFor(AcDefs.Door));
            door.SetFactionDirect(Faction.OfPlayer);
            GenSpawn.Spawn(door, doorCell, map, Rot4.North);

            var interior = rect.ContractedBy(1);
            var floor = AcDefs.Thing("WoodPlankFloor") != null ? TerrainDef.Named("WoodPlankFloor") : null;
            foreach (var cell in interior)
            {
                ClearCell(map, cell);
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                // A floor is most of a room's beauty and all of its cleanliness. Bare dirt is
                // why every room the planner finishes reads "awful" however well it was built.
                if (floor != null) map.terrainGrid.SetTerrain(cell, floor);
            }

            var placed = new List<Thing>();
            for (int i = 0; i < plan.contents.Length; i++)
            {
                var def = AcDefs.Thing(plan.contents[i]);
                if (def == null) continue;

                var thing = PlaceInside(map, interior, def);
                if (thing != null) placed.Add(thing);
            }

            for (int i = 0; i < placed.Count; i++)
            {
                var bed = placed[i] as Building_Bed;
                if (bed == null) continue;
                if (plan.markPrison) MarkAsPrisonBed(bed);
                else if (plan.markMedical) bed.Medical = true;
            }

            var room = rect.CenterCell.GetRoom(map);
            if (room != null)
            {
                room.Notify_RoomShapeChanged();
                foreach (var thing in placed) room.Notify_ContainedThingSpawnedOrDespawned(thing);
            }

            return string.Format("{0} at {1}: placed {2} of {3}",
                                 plan.label, rect.CenterCell, placed.Count, plan.contents.Length);
        }

        /// <summary>Puts one thing anywhere inside the room that the game will accept it.</summary>
        static Thing PlaceInside(Map map, CellRect interior, ThingDef def)
        {
            var rotations = new[] { Rot4.North, Rot4.East };
            foreach (var cell in interior)
            {
                for (int r = 0; r < rotations.Length; r++)
                {
                    if (!GenSpawn.CanSpawnAt(def, cell, map, rotations[r])) continue;

                    var thing = ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
                    thing.SetFactionDirect(Faction.OfPlayer);
                    return GenSpawn.Spawn(thing, cell, map, rotations[r]);
                }
            }
            return null;
        }

        /// <summary>What the game calls a room, against what it was built to be.</summary>
        public static string Verdict(Map map, IntVec3 cell, string expected)
        {
            var room = cell.GetRoom(map);
            if (room == null) return "no room at all";

            string actual = room.Role != null ? room.Role.defName : "none";
            string mark = actual == expected ? "as expected" : "EXPECTED " + expected;

            return string.Format("{0} ({1}) — space {2:0.0}, beauty {3:0.0}, cleanliness {4:0.00}, " +
                                 "impressiveness {5:0.0}",
                                 actual, mark,
                                 room.GetStat(RoomStatDefOf.Space),
                                 room.GetStat(RoomStatDefOf.Beauty),
                                 room.GetStat(RoomStatDefOf.Cleanliness),
                                 room.GetStat(RoomStatDefOf.Impressiveness));
        }

        /// <summary>Everything the colony could build with, removed — to see it go destitute.</summary>
        public static int StripMaterials(Map map)
        {
            var names = new List<string> { "WoodLog", "Steel" };
            names.AddRange(AcDefs.StoneBlockStuff);

            int removed = 0;
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
            return removed;
        }
    }
}
