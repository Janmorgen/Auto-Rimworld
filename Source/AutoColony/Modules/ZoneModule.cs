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
            EnsureCropVariety(ctx);
            EnsureMedicinePlot(ctx);
            EnsureStockpile(ctx);
        }

        /// <summary>
        /// Puts a second crop in the ground once the first field exists.
        ///
        /// Blight destroys a whole crop at once, so a colony living off one large field of one
        /// plant is a single event away from an empty larder — and an empty larder is what sends
        /// colonists out to fight animals for meat, which is where most of the deaths in this
        /// project's test runs actually came from. Different plants also ripen at different
        /// rates, which spreads the harvest instead of staking the season on one week.
        /// </summary>
        void EnsureCropVariety(DirectorContext ctx)
        {
            if (ctx.state.growingCells <= 0) return;
            if (ctx.state.distinctCrops >= Goals.FarmGoal.WantedCrops) return;

            var map = ctx.map;

            // Whatever is already in the ground; the second field must not repeat it.
            var grown = new HashSet<string>();
            foreach (var zone in map.zoneManager.AllZones)
            {
                var g = zone as Zone_Growing;
                if (g == null) continue;
                var plant = g.GetPlantDefToGrow();
                if (plant != null) grown.Add(plant.defName);
            }

            var crop = ChooseCrop(ctx, grown);
            if (crop == null) return;

            int wanted = (int)(ctx.state.colonists * ctx.Gene(Genes.GrowingCellsPerColonist) * 0.4f);
            if (wanted < 12) wanted = 12;

            var cells = FindFertileCells(ctx, wanted);
            if (cells.Count == 0) return;

            var second = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(second);
            second.SetPlantDefToGrow(crop);
            ctx.Credit(BanditId, crop.defName);
            for (int i = 0; i < cells.Count; i++) second.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "second crop planted: {0} across {1} cells, so one blight cannot empty the larder",
                crop.label ?? crop.defName, cells.Count));
        }

        /// <summary>
        /// A herbal medicine plot.
        ///
        /// Healroot is the one crop that is not food and still keeps colonists alive. Without it
        /// a colony treats wounds with nothing at all until it can buy or make real medicine,
        /// and every infection is then a coin toss — which matters here more than it looks,
        /// because a colonist who dies of an untreated wound is also the colonist who was going
        /// to tend everyone else. It needs no research and grows on ordinary soil.
        /// </summary>
        void EnsureMedicinePlot(DirectorContext ctx)
        {
            // `Plant_Healroot`, named outright rather than picked with `??` from a list.
            //
            // The first version wrote `Thing("Plant_HealrootWild") ?? Thing("Plant_Healroot")`,
            // which is the fallback trap this codebase already documents and had already been
            // bitten by once: the wild def resolves perfectly well, so `??` never reaches the
            // second name — and the wild variant is not sowable, so the whole method returned
            // early every pass and no medicine was ever planted. `??` chooses on a def existing,
            // never on it being usable.
            var healroot = AcDefs.Thing("Plant_Healroot");
            if (healroot == null || healroot.plant == null || !healroot.plant.Sowable) return;
            if (!PlacementUtil.ResearchDone(healroot)) return;

            // Healroot needs Plants 8. Sowing it with nobody able to is a zone that stays bare
            // and says nothing about why — the same failure the crop filter was fixed for.
            //
            // Said out loud once, because this is a thing the colony wants and cannot have, and
            // that list is the roadmap. Silence here reads identically to the bug that stopped
            // medicine being planted at all, which is exactly the confusion worth avoiding.
            int skill = BestGrowingSkill(ctx);
            if (skill < healroot.plant.sowMinSkill)
            {
                if (!medicineSkillReported)
                {
                    medicineSkillReported = true;
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "no herbal medicine: healroot needs Plants {0} and the best grower here " +
                        "has {1}, so wounds will be treated with whatever can be bought or found",
                        healroot.plant.sowMinSkill, skill));
                }
                return;
            }
            medicineSkillReported = false;

            var map = ctx.map;
            foreach (var zone in map.zoneManager.AllZones)
            {
                var g = zone as Zone_Growing;
                if (g == null) continue;
                var plant = g.GetPlantDefToGrow();
                if (plant != null && plant.defName == healroot.defName) return;   // already have one
            }

            // Only once the colony is feeding itself. Medicine matters, but not before dinner.
            if (ctx.state.growingCells <= 0) return;

            var cells = FindFertileCells(ctx, MedicinePlotCells);
            if (cells.Count == 0) return;

            var plot = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(healroot);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy,
                "healroot plot sown across " + cells.Count + " cells — herbal medicine without " +
                "research or a trader, so wounds stop being treated with nothing");
        }

        /// <summary>Enough healroot to keep a small colony in herbal medicine, not a cash crop.</summary>
        const int MedicinePlotCells = 24;

        /// <summary>Set once the skill shortfall has been reported, so it is said once, not hourly.</summary>
        bool medicineSkillReported;

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

        ThingDef ChooseCrop(DirectorContext ctx) { return ChooseCrop(ctx, null); }

        /// <summary>
        /// Picks something to sow, optionally excluding what is already in the ground.
        ///
        /// The list is deliberately wider than rice. Rice is fastest and yields least, corn is
        /// the reverse, potatoes tolerate poor soil — which is best depends on the biome, the
        /// season and how hungry the colony is, so it stays a bandit arm rather than a constant.
        /// What is filtered out is only what cannot be sown here at all: unresearched crops, and
        /// ones needing a grower better than anyone the colony has.
        /// </summary>
        ThingDef ChooseCrop(DirectorContext ctx, HashSet<string> exclude)
        {
            var candidates = new List<string>();
            var byName = new Dictionary<string, ThingDef>();

            int bestGrowing = BestGrowingSkill(ctx);

            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def.plant == null || !def.plant.Sowable) continue;
                if (def.plant.harvestedThingDef == null) continue;
                // Food crops only; drugs and textiles are managed elsewhere. Healroot has its
                // own plot, since it is medicine rather than dinner.
                if (!def.plant.harvestedThingDef.IsNutritionGivingIngestible) continue;

                // Skill and research are hard limits, not preferences: a crop nobody can sow is
                // a field that stays bare, and the colony would never find out why.
                if (def.plant.sowMinSkill > bestGrowing) continue;
                if (!PlacementUtil.ResearchDone(def)) continue;

                if (exclude != null && exclude.Contains(def.defName)) continue;

                candidates.Add(def.defName);
                byName[def.defName] = def;
            }

            if (candidates.Count == 0) return null;

            var bandit = ctx.director.BanditFor(BanditId);
            string pick = bandit.Select(candidates, ctx.Gene(Genes.ResearchExplore));
            return pick != null && byName.ContainsKey(pick) ? byName[pick] : null;
        }

        /// <summary>The best Plants skill in the colony, which is what caps what can be sown.</summary>
        static int BestGrowingSkill(DirectorContext ctx)
        {
            int best = 0;
            var colonists = ctx.state.allColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                var pawn = colonists[i];
                if (pawn == null) continue;
                int level = CombatAssessment.SkillLevel(pawn, SkillDefOf.Plants);
                if (level > best) best = level;
            }
            return best;
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
