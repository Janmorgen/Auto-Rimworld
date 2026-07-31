using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Maintains growing and stockpile zones.
    ///
    /// Zone sizing comes from the genome (cells per colonist), while the crop choice is a
    /// bandit arm — rice grows fast but yields little, corn is the opposite, and which one
    /// wins genuinely depends on the biome and the colony's food pressure, so it is exactly
    /// the kind of decision worth learning from outcomes rather than hardcoding.
    /// </summary>
    public class ZoneModule : DirectorModule
    {
        public const string BanditId = "crop";

        public override string Name { get { return "Zones"; } }
        public override int IntervalTicks { get { return 15000; } }

        /// <summary>Minimum soil fertility worth sowing on.</summary>
        const float MinFertility = 0.7f;

        protected override void Act(DirectorContext ctx)
        {
            if (!ctx.layout.established) return;

            EnsureGrowingZone(ctx);
            EnsureStockpile(ctx);
        }

        // ------------------------------------------------------------ growing

        void EnsureGrowingZone(DirectorContext ctx)
        {
            var map = ctx.map;
            int wanted = (int)(ctx.state.colonists * ctx.Gene(Genes.GrowingCellsPerColonist));
            if (wanted <= 0) return;

            int existing = 0;
            Zone_Growing growZone = null;
            foreach (var zone in map.zoneManager.AllZones)
            {
                var g = zone as Zone_Growing;
                if (g == null) continue;
                existing += g.Cells.Count;
                if (growZone == null) growZone = g;
            }

            if (existing >= wanted) return;

            int deficit = wanted - existing;
            // Grow in bounded steps so a big target does not stall the tick in one pass.
            if (deficit > 120) deficit = 120;

            // Find the cells before creating anything: registering a zone and then failing to
            // fill it would leave an empty zone behind, which RimWorld does not expect.
            var cells = FindFertileCells(ctx, deficit);
            if (cells.Count == 0) return;

            if (growZone == null)
            {
                growZone = new Zone_Growing(map.zoneManager);
                map.zoneManager.RegisterZone(growZone);

                var crop = ChooseCrop(ctx);
                if (crop != null)
                {
                    growZone.SetPlantDefToGrow(crop);
                    ctx.Credit(BanditId, crop.defName);
                }
            }

            for (int i = 0; i < cells.Count; i++) growZone.AddCell(cells[i]);
            Note("added " + cells.Count + " growing cells");
        }

        List<IntVec3> FindFertileCells(DirectorContext ctx, int count)
        {
            var map = ctx.map;
            var found = new List<IntVec3>();

            // Spiral outward from the base so fields stay close enough to be worth walking to.
            foreach (var cell in GenRadial.RadialCellsAround(ctx.layout.origin, 40, true))
            {
                if (found.Count >= count) break;
                if (!cell.InBounds(map)) continue;
                if (map.zoneManager.ZoneAt(cell) != null) continue;
                if (cell.GetEdifice(map) != null) continue;
                if (PlacementUtil.HasAnyConstructionAt(map, cell)) continue;
                if (map.fertilityGrid.FertilityAt(cell) < MinFertility) continue;
                if (!cell.Standable(map)) continue;
                if (InsideAnyRoom(ctx, cell)) continue;

                found.Add(cell);
            }

            return found;
        }

        /// <summary>Keeps fields out of the planned building footprint.</summary>
        static bool InsideAnyRoom(DirectorContext ctx, IntVec3 cell)
        {
            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].Rect.Contains(cell)) return true;
            return false;
        }

        ThingDef ChooseCrop(DirectorContext ctx)
        {
            var candidates = new List<string>();
            var byName = new Dictionary<string, ThingDef>();

            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def.plant == null || !def.plant.Sowable) continue;
                if (def.plant.harvestedThingDef == null) continue;
                // Food crops only; drugs and textiles are managed elsewhere.
                if (!def.plant.harvestedThingDef.IsNutritionGivingIngestible) continue;
                if (def.plant.sowMinSkill > 6) continue;

                candidates.Add(def.defName);
                byName[def.defName] = def;
            }

            if (candidates.Count == 0) return null;

            var bandit = ctx.director.BanditFor(BanditId);
            string pick = bandit.Select(candidates, ctx.Gene(Genes.ResearchExplore));
            return pick != null && byName.ContainsKey(pick) ? byName[pick] : null;
        }

        // ------------------------------------------------------------ stockpile

        void EnsureStockpile(DirectorContext ctx)
        {
            var map = ctx.map;
            int wanted = (int)(ctx.state.colonists * ctx.Gene(Genes.StockpileCellsPerColonist));
            if (wanted <= 0) return;

            int existing = 0;
            Zone_Stockpile pile = null;
            foreach (var zone in map.zoneManager.AllZones)
            {
                var sp = zone as Zone_Stockpile;
                if (sp == null) continue;
                existing += sp.Cells.Count;
                if (pile == null) pile = sp;
            }

            if (existing >= wanted) return;

            int deficit = wanted - existing;
            if (deficit > 60) deficit = 60;

            var cells = FindStockpileCells(ctx, deficit);
            if (cells.Count == 0) return;

            if (pile == null)
            {
                pile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(pile);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                pile.AddCell(cells[i]);
                PlacementUtil.MarkHome(map, cells[i]);
            }
            Note("added " + cells.Count + " stockpile cells");
        }

        List<IntVec3> FindStockpileCells(DirectorContext ctx, int count)
        {
            var map = ctx.map;
            var found = new List<IntVec3>();

            // Prefer the interior of a room reserved for storage; fall back to open ground
            // near the base if none has been built yet.
            var storage = FindRoom(ctx, RoomRole.Storage);
            if (storage != null)
            {
                foreach (var cell in storage.Interior)
                {
                    if (found.Count >= count) break;
                    if (CellUsable(map, cell)) found.Add(cell);
                }
                if (found.Count > 0) return found;
            }

            foreach (var cell in GenRadial.RadialCellsAround(ctx.layout.origin, 15, true))
            {
                if (found.Count >= count) break;
                if (InsideAnyRoom(ctx, cell)) continue;
                if (CellUsable(map, cell)) found.Add(cell);
            }

            return found;
        }

        static bool CellUsable(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            if (map.zoneManager.ZoneAt(cell) != null) return false;
            if (cell.GetEdifice(map) != null) return false;
            return cell.Standable(map);
        }

        static PlannedRoom FindRoom(DirectorContext ctx, RoomRole role)
        {
            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].role == role) return rooms[i];
            return null;
        }
    }
}
