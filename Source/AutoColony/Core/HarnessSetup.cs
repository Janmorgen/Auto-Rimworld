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
