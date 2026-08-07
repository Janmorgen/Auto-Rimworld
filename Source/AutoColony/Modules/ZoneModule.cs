using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Plants;
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
            EnsureTextilePlot(ctx);
            EnsureFodderPlot(ctx);
            EnsureWoodPlot(ctx);
            EnsureSocialPlot(ctx);
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
            ctx.Credit(BanditId, crop.defName, "Food security");
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
                // Into the list as well as into the log. The comment below used to end at "that
                // list is the roadmap" and no list existed: the message was printed once, a bool
                // stopped it repeating, and a gap thirteen days old read exactly like one found
                // this minute. See CapabilityGaps and run 170.
                CapabilityGaps.Report("herbal medicine", "Plants",
                                      healroot.plant.sowMinSkill, skill, ctx.state.tick);

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

            if (CapabilityGaps.IsOpen("herbal medicine"))
            {
                Chronicle.Record(ChronicleCategory.Economy, string.Format(
                    "herbal medicine is in reach at last — Plants {0} against the {1} healroot " +
                    "needs, after {2:0.0} days of doing without. Closing a gap is as much a fact " +
                    "as opening one",
                    skill, healroot.plant.sowMinSkill,
                    CapabilityGaps.StandingFor("herbal medicine", ctx.state.tick) / 60000f));
                CapabilityGaps.Close("herbal medicine");
            }
            medicineSkillReported = false;

            var map = ctx.map;

            // Sized by the colony and widened when it runs short, rather than sown once at a
            // fixed 24 cells and never thought about again.
            //
            // Medicine was the only quantity in ColonyState that nothing acted on. It was
            // measured map-wide, printed in the vitals, and read by exactly one caller — the
            // player model — so "med 0" and "med 30" produced identical behaviour. Run 110 hit
            // med 0 with the game's own Low medicine alert up, a heatstroke casualty, and a
            // plot the right size for a colony that was not short.
            //
            // "Short" is RimWorld's own line: Alert_LowMedicine fires below
            // MedicinePerColonistThreshold, which is 2 per colonist. Borrowing it means the
            // colony starts growing more at the moment the player's screen starts warning, and
            // nobody here has to invent a number.
            bool short_ = ctx.state.medicineCount < LowMedicinePerColonist * ctx.state.colonists;
            int wanted = System.Math.Max(MedicinePlotCells,
                                         ctx.state.colonists * MedicineCellsPerColonist);
            if (short_) wanted *= 2;

            var standing = FindZoneGrowing(ctx, healroot);
            if (standing != null)
            {
                int have = standing.Cells.Count;
                if (have >= wanted) return;

                var more = FindFertileCells(ctx, wanted - have);
                for (int i = 0; i < more.Count; i++) standing.AddCell(more[i]);
                if (more.Count > 0)
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "widened the healroot plot by {0} cells — {1} medicine for {2} colonists, " +
                        "and the game calls anything under {3} apiece low; healroot takes about " +
                        "eleven days, so the time to sow more is before the last one is used",
                        more.Count, ctx.state.medicineCount, ctx.state.colonists,
                        LowMedicinePerColonist));
                return;
            }

            // Only once the colony is feeding itself. Medicine matters, but not before dinner.
            if (ctx.state.growingCells <= 0) return;

            var cells = FindFertileCells(ctx, wanted);
            if (cells.Count == 0) return;

            var plot = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(healroot);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy,
                "healroot plot sown across " + cells.Count + " cells — herbal medicine without " +
                "research or a trader, so wounds stop being treated with nothing");
        }

        /// <summary>
        /// Trees, where the map has too few of them.
        ///
        /// The other half of WoodSupplyGoal: the goal carries the research, this plants the
        /// thing. Wood is the only fuel a pre-electric colony has, and on a map that starts
        /// with a handful of trees the woodpile is a countdown rather than a stock — run 110
        /// reached day 20 with eight dry hoppers and nothing left standing to cut.
        ///
        /// Only once the research is done and the map is actually poor in wood. On a forested
        /// map this never fires, because chopping what is already there is faster than growing
        /// it and the colony has better uses for the ground.
        ///
        /// The tree is chosen by what it yields rather than by name, so whatever the biome and
        /// the mods offer, the one that grows fastest for the wood wins.
        /// </summary>
        void EnsureWoodPlot(DirectorContext ctx)
        {
            var s = ctx.state;
            if (s.burners <= 0) return;               // nothing burns; no reason to farm fuel
            if (s.growingWood) return;                // already raising it
            if (s.fuelOnHand >= WoodComfortable) return;
            if (s.growingCells <= 0) return;          // dinner before timber

            var tree = FastestWoodTree(ctx);

            // Belt and braces against the runaway that just happened.
            //
            // growingWood is a state flag computed elsewhere, and when it silently stopped being
            // set this sowed twenty-eight forty-cell plots in a single run. Asking the map
            // directly whether this plant already has a zone cannot go wrong the same way — it
            // is the same check EnsureFodderPlot has always used, and the reason that one has
            // never duplicated.
            if (tree != null && FindZoneGrowing(ctx, tree) != null) return;
            if (tree == null)
            {
                if (!woodReported)
                {
                    woodReported = true;
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "wood is short ({0} standing for {1} fires) and no tree can be sown yet — " +
                        "tree sowing is 1000 points, Neolithic, and needs nothing before it",
                        s.fuelOnHand, s.burners));
                }
                return;
            }
            woodReported = false;

            var cells = FindFertileCells(ctx, WoodPlotCells);

            // A one-cell woodlot is not a wood supply, and worse, it answers the goal.
            //
            // Run 118 sowed "saguaro cactus plot across 1 cells" on sand — forty cells asked
            // for, one fertile cell found — and WoodSupplyGoal then read growingWood and stood
            // down satisfied. A token gesture that switches off the thing watching for the
            // problem is worse than doing nothing, because the colony stops looking.
            //
            // So a plot is either big enough to matter or it is not sown, and the reason is said
            // out loud: on a map with no fertile ground the honest answer is that wood cannot be
            // grown here, which is information rather than a failure to report.
            if (cells.Count < MinWoodPlotCells)
            {
                if (!woodGroundReported)
                {
                    woodGroundReported = true;
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "wood cannot be grown here — {0} fertile cells found of {1} wanted, and a " +
                        "plot that small feeds nothing; the fires run on what is standing and " +
                        "what can be traded for",
                        cells.Count, WoodPlotCells));
                }
                return;
            }
            woodGroundReported = false;

            var plot = new Zone_Growing(ctx.map.zoneManager);
            ctx.map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(tree);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "{0} plot sown across {1} cells — {2} wood standing for {3} things that burn it, " +
                "and a map this bare does not grow it back on its own",
                tree.label ?? tree.defName, cells.Count, s.fuelOnHand, s.burners));
        }

        /// <summary>
        /// The sowable tree that yields fuel soonest, by wood per growing day.
        ///
        /// Asked of the defs rather than picked: harvestedThingDef says what it gives,
        /// sowMinSkill and sowResearchPrerequisites say whether the colony may sow it, and
        /// harvestYield over growDays says how fast it pays. Poplar wins in vanilla; nothing
        /// here needs to know that.
        /// </summary>
        ThingDef FastestWoodTree(DirectorContext ctx)
        {
            ThingDef best = null;
            float bestRate = 0f;
            int skill = BestGrowingSkill(ctx);

            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || def.plant == null) continue;
                if (!def.plant.Sowable || !def.plant.IsTree) continue;
                if (def.plant.harvestedThingDef == null) continue;
                if (def.plant.harvestYield <= 0f || def.plant.growDays <= 0f) continue;
                if (skill < def.plant.sowMinSkill) continue;
                if (!PlacementUtil.ResearchDone(def)) continue;

                float rate = def.plant.harvestYield / def.plant.growDays;
                if (rate <= bestRate) continue;
                bestRate = rate;
                best = def;
            }
            return best;
        }

        /// <summary>Standing wood above which growing more is not worth the ground.</summary>
        const int WoodComfortable = 400;

        /// <summary>A woodlot, not a forestry industry.</summary>
        const int WoodPlotCells = 40;

        /// <summary>
        /// Below this a plot is a gesture rather than a supply — and a gesture that satisfies
        /// WoodSupplyGoal, which is the harm. A quarter of what was asked for.
        /// </summary>
        const int MinWoodPlotCells = 10;

        bool woodReported;
        bool woodGroundReported;

        /// <summary>Enough healroot to keep a small colony in herbal medicine, not a cash crop.</summary>
        const int MedicinePlotCells = 24;

        /// <summary>Plot cells per colonist, so the plot grows with the people it treats.</summary>
        const int MedicineCellsPerColonist = 10;

        /// <summary>
        /// RimWorld's own <c>Alert_LowMedicine.MedicinePerColonistThreshold</c>, read out of the
        /// assembly rather than guessed. Below this the player gets a Low medicine warning, so
        /// below this is where the colony should already be growing more.
        /// </summary>
        const int LowMedicinePerColonist = 2;

        /// <summary>Cloth for a coat each, roughly, rather than a textile industry.</summary>
        const int TextilePlotCells = 30;

        /// <summary>Hay to carry a small herd through one lean season.</summary>
        const int FodderPlotCells = 40;

        bool textileReported;
        bool fodderReported;

        /// <summary>A comfort crop, not a cash crop.</summary>
        const int SocialPlotCells = 24;

        /// <summary>
        /// Something to take the edge off, when there is slack to grow it and a way to use it.
        ///
        /// Drug crops are permissible and they do real work — beer, smokeleaf and psychite all
        /// carry mood, and mood is what six of this project's colonies actually died of. What
        /// they are not is food, which is the mistake that started all of this.
        ///
        /// Three guards, and each is a lesson already paid for:
        ///
        /// The colony must be able to *process* it. A field of psychoid leaves is worth nothing
        /// without Drug Production; that is the original error wearing a different hat, and
        /// CanProcess asks the recipe graph rather than assuming.
        ///
        /// There must be food first, with margin. Ground under a comfort crop is ground not
        /// under potatoes, and a hungry colony has no business growing psychite.
        ///
        /// And it must be wanted. Mood is checked on the worst colonist rather than the average,
        /// because breaks are an individual event and in a colony of three a contented pair
        /// hides the person about to go berserk.
        /// </summary>
        void EnsureSocialPlot(DirectorContext ctx)
        {
            if (ctx.state.growingCells <= 0) return;
            if (!ctx.state.growingSeasonNow) return;           // see EnsureTextilePlot
            if (ctx.state.daysOfFood < 5f) return;              // dinner, with margin
            if (ctx.state.minMood > 0.45f) return;              // nobody is struggling

            var crop = BestOfRole(ctx, PlantRole.Social);
            if (crop == null || !PlantTaxonomy.CanProcess(crop)) return;
            if (AlreadyGrowing(ctx, crop)) return;

            var cells = FindFertileCells(ctx, SocialPlotCells);
            if (cells.Count == 0) return;

            var map = ctx.map;
            var plot = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(crop);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "{0} sown across {1} cells — the worst mood here is {2:0.00} and there are {3:0.0} " +
                "days of food, so there is room to grow something that is not dinner",
                crop.label ?? crop.defName, cells.Count, ctx.state.minMood, ctx.state.daysOfFood));
        }

        /// <summary>
        /// Cloth, because colonists have frozen to death for want of a coat.
        ///
        /// Run 72 lost two people to Hypothermia (extreme) while the colony dutifully made
        /// tribalwear, and the whole chain behind that has been unpicked since — but every
        /// version of it ends at cloth, and nothing in the director has ever grown any. Textiles
        /// were measured (ColonyState.textiles), given a production target
        /// (Genes.TextilesTarget) and used in bills, with no source on the map at all: a colony
        /// with no trader and no cotton simply never had cloth to sew.
        ///
        /// Only when there is little in store. A field of cotton is worth nothing to a colony
        /// that already has bolts of it, and the cells are better under food.
        /// </summary>
        void EnsureTextilePlot(DirectorContext ctx)
        {
            if (ctx.state.growingCells <= 0) return;              // dinner first
            if (ctx.state.textiles >= 120) return;                // enough to sew with

            // And not into a season that will not grow it.
            //
            // Run 97 sowed thirty cells of cotton on a map reading "Fall, nothing grows
            // outdoors, -31C", then lost two colonists to Hypothermia (extreme) with the field
            // still bare. A crop that cannot reach harvest is the same waste as one nobody can
            // eat or process — soil, sowing and hauling spent for nothing — and it is worse
            // here, because the thing it was meant to produce is what they died without.
            if (!ctx.state.growingSeasonNow) return;

            var crop = BestOfRole(ctx, PlantRole.Textile);
            if (crop == null)
            {
                // Cotton needs no research; devilstrand does. If neither is available it is
                // worth saying, because "the colony never made clothes" and "the colony could
                // not grow anything to make them from" look identical from outside.
                if (!textileReported)
                {
                    textileReported = true;
                    Chronicle.Record(ChronicleCategory.Economy,
                        "no textile crop can be sown here, so cloth has to be bought or taken — " +
                        "which is the same corner run 72 froze in");
                }
                return;
            }

            if (AlreadyGrowing(ctx, crop)) return;

            var cells = FindFertileCells(ctx, TextilePlotCells);
            if (cells.Count == 0) return;

            var map = ctx.map;
            var plot = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(crop);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "{0} sown across {1} cells — the colony holds {2} cloth, and a tailor bench with " +
                "nothing to sew is a colonist in tribalwear at -12C",
                crop.label ?? crop.defName, cells.Count, ctx.state.textiles));
        }

        /// <summary>
        /// Hay, for animals the pen cannot feed all year.
        ///
        /// The pen report already names this exact shortfall — "the herd eats 0.80 a day and
        /// winter forages only 0.00, so this pen needs hay hauled to it for one season a year"
        /// — and until now nothing could act on it, because the colony had no way to produce
        /// hay. Grazing is free while the grass grows; the question a pen asks is what happens
        /// when it stops.
        ///
        /// Only with animals to feed. Hay is worth nothing to a colony that has none, and
        /// haygrass is the least useful thing that can occupy fertile soil otherwise.
        /// </summary>
        void EnsureFodderPlot(DirectorContext ctx)
        {
            if (ctx.state.growingCells <= 0) return;
            if (ctx.state.tamedAnimals <= 0) return;
            if (!ctx.state.growingSeasonNow) return;   // see EnsureTextilePlot

            var crop = BestOfRole(ctx, PlantRole.Fodder);
            if (crop == null)
            {
                if (!fodderReported)
                {
                    fodderReported = true;
                    Chronicle.Record(ChronicleCategory.Economy,
                        "animals to feed and no fodder crop that will grow here — they live on " +
                        "what the pen forages or they do not live");
                }
                return;
            }

            // Sized by the herd, and widened as it grows. The pen is fenced once for the
            // animals standing there on the day; every animal bought, born or tamed after that
            // deepens a winter shortfall the pen report already names and nothing was answering.
            // The fodder plot is the answer that needs no fence surgery: hay scales by adding
            // cells, and the plot is re-checked every pass against the herd that exists now.
            int wanted = System.Math.Max(FodderPlotCells,
                (int)(ctx.state.tamedAnimals * ctx.Gene(Genes.FodderCellsPerAnimal)));

            var existing = FindZoneGrowing(ctx, crop);
            if (existing != null)
            {
                int have = existing.Cells.Count;
                if (have >= wanted) return;

                var extra = FindFertileCells(ctx, wanted - have);
                for (int i = 0; i < extra.Count; i++) existing.AddCell(extra[i]);
                if (extra.Count > 0)
                    Chronicle.Record(ChronicleCategory.Economy, string.Format(
                        "widened the {0} plot by {1} cells for a herd of {2} — the pen was fenced " +
                        "for fewer animals than stand in it now, and hay is how the gap is fed",
                        crop.label ?? crop.defName, extra.Count, ctx.state.tamedAnimals));
                return;
            }

            var cells = FindFertileCells(ctx, wanted);
            if (cells.Count == 0) return;

            var map = ctx.map;
            var plot = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(plot);
            plot.SetPlantDefToGrow(crop);
            for (int i = 0; i < cells.Count; i++) plot.AddCell(cells[i]);

            Chronicle.Record(ChronicleCategory.Economy, string.Format(
                "{0} sown across {1} cells for {2} animals — a pen feeds itself while the grass " +
                "grows, and this is what they eat when it stops",
                crop.label ?? crop.defName, cells.Count, ctx.state.tamedAnimals));
        }

        /// <summary>
        /// The best sowable plant of a given role — cheapest to grow that the colony can
        /// actually sow. Skill and research are hard limits, as everywhere else here: a crop
        /// nobody can sow is a field that stays bare and never says why.
        /// </summary>
        static ThingDef BestOfRole(DirectorContext ctx, PlantRole role)
        {
            int skill = BestGrowingSkill(ctx);
            ThingDef best = null;

            var sowable = PlantTaxonomy.Sowable();
            for (int i = 0; i < sowable.Count; i++)
            {
                var def = sowable[i];
                if (PlantTaxonomy.RoleOf(def) != role) continue;
                if (def.plant.sowMinSkill > skill) continue;
                if (!PlacementUtil.ResearchDone(def)) continue;

                // Fastest to a harvest wins. Devilstrand is better cloth and takes most of a
                // year; a colony that is cold now needs cotton now.
                if (best == null || def.plant.growDays < best.plant.growDays) best = def;
            }
            return best;
        }

        static Zone_Growing FindZoneGrowing(DirectorContext ctx, ThingDef crop)
        {
            foreach (var zone in ctx.map.zoneManager.AllZones)
            {
                var g = zone as Zone_Growing;
                if (g == null) continue;
                var plant = g.GetPlantDefToGrow();
                if (plant != null && plant.defName == crop.defName) return g;
            }
            return null;
        }

        static bool AlreadyGrowing(DirectorContext ctx, ThingDef crop)
        {
            foreach (var zone in ctx.map.zoneManager.AllZones)
            {
                var g = zone as Zone_Growing;
                if (g == null) continue;
                var plant = g.GetPlantDefToGrow();
                if (plant != null && plant.defName == crop.defName) return true;
            }
            return false;
        }

        /// <summary>Set once the skill shortfall has been reported, so it is said once, not hourly.</summary>
        bool medicineSkillReported;

        // ------------------------------------------------------------ growing

        /// <summary>Ground glow a plant needs to grow. Below this it simply sits there.</summary>
        const float GrowingLight = 0.51f;

        /// <summary>
        /// Takes back growing cells that have ended up in the dark.
        ///
        /// The placement search already refuses cells inside a planned room, and it cannot help,
        /// because the ordering runs both ways: a field is laid on day nought and a room is
        /// sited over it a week later, or a room is planned and the field laid across its
        /// blueprints — which is where "Added zone over zone-incompatible thing Blueprint" in
        /// the warning log was coming from.
        ///
        /// Either way the cells end up roofed, and a roofed cell grows nothing without a
        /// powered sun lamp over it. The colony then tends a field that cannot produce, and the
        /// growing-cell count says the food problem is solved.
        ///
        /// So this is a maintenance pass rather than a placement rule: whatever the reason a
        /// cell went dark, it stops being a field.
        /// </summary>
        void ReleaseDarkenedFields(DirectorContext ctx)
        {
            var map = ctx.map;
            if (map.zoneManager == null || map.glowGrid == null) return;

            // Snapshot the zone list before touching any of it.
            //
            // Removing a growing zone's last cell makes the game deregister the zone, which
            // mutates AllZones underneath the loop that is walking it — "Collection was
            // modified", and the Zones module dies for that pass. The inner loop already
            // defers cell removal for the same reason one level down; the outer one did not,
            // because until this evening a colony rarely had more than two growing zones and
            // never lost a whole one to a roof.
            //
            // Adding cotton, haygrass, healroot and social plots made a fully-darkened zone
            // several times likelier. The bug was always there; the new plots bought the ticket.
            var zones = new List<Zone>(map.zoneManager.AllZones);

            int released = 0;
            for (int z = 0; z < zones.Count; z++)
            {
                var g = zones[z] as Zone_Growing;
                if (g == null) continue;

                var doomed = new List<IntVec3>();
                foreach (var cell in g.Cells)
                {
                    if (!cell.InBounds(map)) continue;
                    if (!map.roofGrid.Roofed(cell)) continue;          // open sky is fine
                    if (map.glowGrid.GroundGlowAt(cell) >= GrowingLight) continue;  // lamp over it
                    doomed.Add(cell);
                }

                for (int i = 0; i < doomed.Count; i++) { g.RemoveCell(doomed[i]); released++; }
            }

            if (released > 0)
            {
                Chronicle.Record(ChronicleCategory.Economy, string.Format(
                    "took {0} growing cells back out of the field — they have ended up under a " +
                    "roof with no sun lamp over them, where nothing grows however long it is tended",
                    released));
                Note("released " + released + " darkened growing cells");
            }
        }

        void EnsureGrowingZone(DirectorContext ctx)
        {
            var map = ctx.map;
            ReleaseDarkenedFields(ctx);
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
                    ctx.Credit(BanditId, crop.defName, "Food security");
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
                // Dinner, and nothing that merely looks like it.
                //
                // Two attempts before this one filtered here by hand and both let psychoid
                // through — first because its leaves give nutrition, then because they are not
                // classed as a drug. A colony planted seventy-two cells of the stuff as
                // insurance against blight, lost its rice to blight, and starved beside the
                // field. PlantTaxonomy answers the question properly now, and answers it the
                // same way everywhere the question is asked.
                // Sowable first, and not as a detail. Replacing the old hand-rolled filters
                // with the taxonomy dropped this check with them, and the very next colony
                // planted seventy-two cells of agarilux — a wild cave plant that is genuinely
                // food by every test the taxonomy applies and cannot be sown in a field at all.
                // The category was right and the plant was unplantable, which is its own kind
                // of wrong answer.
                if (def.plant == null || !def.plant.Sowable) continue;
                if (def.plant.harvestedThingDef == null) continue;

                if (PlantTaxonomy.RoleOf(def) != PlantRole.Food) continue;

                // Skill and research are hard limits, not preferences: a crop nobody can sow is
                // a field that stays bare, and the colony would never find out why.
                if (def.plant.sowMinSkill > bestGrowing) continue;
                if (!PlacementUtil.ResearchDone(def)) continue;

                if (exclude != null && exclude.Contains(def.defName)) continue;

                candidates.Add(def.defName);
                byName[def.defName] = def;
            }

            if (candidates.Count == 0) return null;

            candidates = OnlyWhatArrivesInTime(ctx, candidates, byName);

            var bandit = ctx.director.BanditFor(BanditId);
            string pick = bandit.Select(candidates, ctx.Gene(Genes.ResearchExplore));
            return pick != null && byName.ContainsKey(pick) ? byName[pick] : null;
        }

        /// <summary>
        /// While the colony is short of food, drops the crops that cannot ripen in time.
        ///
        /// Rice takes three days to grow, potatoes six, corn eleven — and which is *best* depends
        /// on biome and pressure, which is exactly why the choice is a bandit arm. But which is
        /// best and which is survivable are different questions, and only the second one matters
        /// with an empty larder. A colony sowed seventy-two cells of corn on day zero at 0.0 days
        /// of food and starved on day two; the corn would have been ready on day eleven.
        ///
        /// So this narrows the arms rather than overriding the choice. The bandit still learns
        /// which crop wins; it is simply not offered one the colony will not live to harvest.
        /// </summary>
        static List<string> OnlyWhatArrivesInTime(DirectorContext ctx, List<string> candidates,
                                                  Dictionary<string, ThingDef> byName)
        {
            float urgency = FoodTiming.Urgency(ctx.state.daysOfFood,
                                               ctx.FoodDaysWanted);
            if (urgency < 0.5f) return candidates;   // comfortable: any crop is a fair bet

            float fastest = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                float days = byName[candidates[i]].plant.growDays;
                if (days < fastest) fastest = days;
            }
            if (fastest >= float.MaxValue) return candidates;

            // Within half again the quickest thing available. Loose enough that the bandit keeps
            // a real choice where several crops are comparable, tight enough to exclude the ones
            // that are three or four times slower.
            float limit = fastest * 1.5f;

            var inTime = new List<string>();
            for (int i = 0; i < candidates.Count; i++)
                if (byName[candidates[i]].plant.growDays <= limit) inTime.Add(candidates[i]);

            return inTime.Count > 0 ? inTime : candidates;
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

            // Nothing on its way here either.
            //
            // The fertile-cell search has always checked this and the stockpile search never
            // did, though they share this test — and the stockpile search prefers the *interior
            // of the storage room*, which is exactly where the planner is about to blueprint
            // its shelves. So the zone went down on top of them, and the game said so twenty
            // times a colony: "Added zone over zone-incompatible thing Blueprint_Shelf". Nothing
            // was counting warnings, so nobody heard it.
            if (PlacementUtil.HasAnyConstructionAt(map, cell)) return false;

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
