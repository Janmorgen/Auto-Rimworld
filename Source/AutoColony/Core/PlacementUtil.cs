using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Blueprint placement helpers.
    ///
    /// Everything here validates before it acts and returns false rather than throwing: the
    /// planner speculatively tries a great many cells, and a rejected placement is a normal
    /// outcome, not an error.
    /// </summary>
    public static class PlacementUtil
    {
        /// <summary>True if the cell already holds a building, blueprint, or frame.</summary>
        public static bool HasConstructionAt(Map map, IntVec3 cell, ThingDef def)
        {
            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                if (t == null) continue;
                if (t.def == def) return true;

                var bp = t as Blueprint;
                if (bp != null && bp.def.entityDefToBuild == def) return true;

                var frame = t as Frame;
                if (frame != null && frame.def.entityDefToBuild == def) return true;
            }
            return false;
        }

        /// <summary>Any blueprint or frame at all, regardless of what it builds.</summary>
        public static bool HasAnyConstructionAt(Map map, IntVec3 cell)
        {
            var things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint || things[i] is Frame) return true;
            }
            return false;
        }

        /// <summary>
        /// Places a build blueprint if the game will accept it. Returns false when the spot is
        /// blocked, already queued, or the def cannot legally go there.
        /// </summary>
        public static bool TryPlace(Map map, ThingDef def, IntVec3 cell, Rot4 rot, ThingDef stuff)
        {
            if (map == null || def == null) return false;
            if (!cell.InBounds(map)) return false;
            if (HasConstructionAt(map, cell, def)) return false;
            if (HasAnyConstructionAt(map, cell)) return false;

            if (def.MadeFromStuff && stuff == null) return false;
            if (!def.MadeFromStuff) stuff = null;

            var report = GenConstruct.CanPlaceBlueprintAt(def, cell, rot, map, false, null, null, stuff);
            if (!report.Accepted) return false;

            GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff);
            return true;
        }

        /// <summary>
        /// Picks a construction material the colony can actually afford right now.
        /// <paramref name="stonePreference"/> in [0,1] biases between wood and stone blocks —
        /// wood is fast and cheap, stone is slower but will not burn.
        /// </summary>
        public static ThingDef ChooseStuff(Map map, ThingDef def, float stonePreference)
        {
            bool ignored;
            return ChooseStuff(map, def, stonePreference, out ignored);
        }

        /// <summary>
        /// As above, reporting whether the preferred material was actually available.
        ///
        /// Worth surfacing: a colony on day one prefers stone and builds in wood because it has
        /// no cut blocks yet, which is correct but looks like the preference being ignored. A
        /// log that cannot tell those apart is a log that invites the wrong fix.
        /// </summary>
        public static ThingDef ChooseStuff(Map map, ThingDef def, float stonePreference,
                                           out bool gotPreferred)
        {
            gotPreferred = true;
            if (def == null || !def.MadeFromStuff) return null;

            var order = new List<string>();
            if (stonePreference >= 0.5f)
            {
                order.AddRange(AcDefs.StoneBlockStuff);
                order.AddRange(AcDefs.WoodyStuff);
            }
            else
            {
                order.AddRange(AcDefs.WoodyStuff);
                order.AddRange(AcDefs.StoneBlockStuff);
            }
            order.AddRange(AcDefs.MetalStuff);

            int needed = def.CostStuffCount > 0 ? def.CostStuffCount : 1;
            bool preferStone = stonePreference >= 0.5f;

            for (int i = 0; i < order.Count; i++)
            {
                var stuff = AcDefs.Thing(order[i]);
                if (stuff == null || stuff.stuffProps == null) continue;
                if (!SharesAny(def.stuffCategories, stuff.stuffProps.categories)) continue;
                // Keep a reserve so building never consumes the last of a material.
                if (AvailableCount(map, stuff) < needed * 3) continue;

                bool isStone = System.Array.IndexOf(AcDefs.StoneBlockStuff, stuff.defName) >= 0;
                gotPreferred = isStone == preferStone;
                return stuff;
            }

            gotPreferred = false;

            // Nothing comfortably affordable; fall back to whatever the game would default to
            // so early colonies can still put up their first walls.
            return GenStuff.DefaultStuffFor(def);
        }

        /// <summary>
        /// How much of a material the colony can actually build with.
        ///
        /// <c>ResourceCounter</c> only counts what is in a stockpile, which is nothing at all
        /// on the first day — so material preference was being silently ignored for the first
        /// few rooms and every choice fell through to the game's default. Colonists will haul
        /// from anywhere, so loose stacks count too.
        /// </summary>
        public static int AvailableCount(Map map, ThingDef stuff)
        {
            if (map == null || stuff == null) return 0;

            int total = map.resourceCounter != null ? map.resourceCounter.GetCount(stuff) : 0;
            if (total > 0) return total;

            var loose = map.listerThings != null ? map.listerThings.ThingsOfDef(stuff) : null;
            if (loose == null) return total;

            for (int i = 0; i < loose.Count; i++)
            {
                var thing = loose[i];
                if (thing == null || !thing.Spawned) continue;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                total += thing.stackCount;
            }
            return total;
        }

        /// <summary>Marks a cell as part of the home area so colonists will tend and clean it.</summary>
        public static void MarkHome(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return;
            var home = map.areaManager.Home;
            if (home != null) home[cell] = true;
        }

        /// <summary>Requests a roof over a cell once its walls exist.</summary>
        public static void MarkRoof(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map)) return;
            var roof = map.areaManager.BuildRoof;
            if (roof != null) roof[cell] = true;
        }

        /// <summary>
        /// Rough test that an area is worth building on: in bounds, mostly standable,
        /// and not water. Used to choose where the base goes.
        /// </summary>
        public static float BuildableFraction(Map map, CellRect rect)
        {
            int total = 0, good = 0;
            foreach (var c in rect)
            {
                if (!c.InBounds(map)) return 0f;
                total++;

                var terrain = c.GetTerrain(map);
                if (terrain == null) continue;
                if (terrain.passability == Traversability.Impassable) continue;
                if (!terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy)) continue;
                if (c.GetEdifice(map) != null) continue;

                good++;
            }
            return total > 0 ? good / (float)total : 0f;
        }

        static bool SharesAny<T>(List<T> a, List<T> b)
        {
            if (a == null || b == null) return false;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < b.Count; j++)
                    if (Equals(a[i], b[j])) return true;
            return false;
        }
    }
}
