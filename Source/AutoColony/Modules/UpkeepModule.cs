using System;
using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Upkeep;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Fixes what the colony has already built.
    ///
    /// Every other construction path in the director adds: it reserves a room and fills it. This
    /// one is the only thing that changes its mind about something standing — it roofs an
    /// exposed generator, lights a dark bedroom, pulls the surplus beds out of a barracks, and
    /// where a building simply cannot stay where it is, takes it down so the planner puts it
    /// back somewhere sensible.
    ///
    /// It acts on one defect per pass on purpose. Remedies queue colonist work, and a survey
    /// that found eleven problems and ordered all eleven at once would bury the construction the
    /// colony actually needs under a backlog of tidying.
    /// </summary>
    public class UpkeepModule : DirectorModule
    {
        public override string Name { get { return "Upkeep"; } }

        // Roughly twice an in-game day. These are slow-burning problems, and re-surveying often
        // costs a room walk per colonist for something that changes over days.
        public override int IntervalTicks { get { return 15000; } }

        // Deliberately *not* Discretionary. Most upkeep should wait while the colony is
        // burning, but "most" is not "all", and switching the whole module off during an
        // emergency is how a colony that lurched from one crisis to the next never got round to
        // burying anyone — carrying the largest penalty in the game for eleven days over a
        // building that costs nothing. The bar rises instead of the work stopping.

        /// <summary>How well a fix has to pay for itself to be worth doing mid-crisis.</summary>
        const float UrgentOnly = 0.8f;

        readonly List<UnmetComplaint> unhandled = new List<UnmetComplaint>();

        float[] kindWeights;

        /// <summary>
        /// This colony's own opinion of what each kind of fault is worth, read from its genome.
        /// Rebuilt each pass because a training trial swaps the genome underneath the module.
        /// </summary>
        float[] UpkeepWeights(DirectorContext ctx)
        {
            if (kindWeights == null) kindWeights = new float[DefectPolicy.KindCount];
            for (int i = 0; i < kindWeights.Length; i++)
                kindWeights[i] = ctx.Gene(DefectPolicy.WeightKey((DefectKind)i));
            return kindWeights;
        }

        protected override void Act(DirectorContext ctx)
        {

            // While something immediate is happening the colony only does what clearly pays for
            // itself — burying the dead does, decorating does not.
            bool crisis = ctx.state.EmergencyAtHome ||
                          (ctx.plan != null && ctx.plan.EmergencyActive);
            float bar = crisis ? UrgentOnly : DefectPolicy.ActionThreshold;

            // Withdraw anything the colony asked for and no longer wants, before asking for more.
            if (!crisis && CancelStaleOrders(ctx)) return;

            unhandled.Clear();
            var defects = DefectSurvey.Survey(ctx.map, ctx.state, ctx.layout, unhandled,
                                              ctx.Gene(Genes.RoomEssentialWeight),
                                              ctx.Gene(Genes.RoomOccupancyWeight),
                                              UpkeepWeights(ctx));

            Report(ctx, BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists),
                   defects.Count);
            if (defects.Count == 0) return;

            for (int i = 0; i < defects.Count; i++)
            {
                var defect = defects[i];
                if (defect.Priority < bar) continue;
                if (!DefectPolicy.WorthActing(defect.kind, defect.severity)) continue;
                if (!Apply(ctx, defect)) continue;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "upkeep — {0}: {1} ({2}, severity {3:0.00})",
                    defect.remedy, defect.what, defect.kind, defect.severity));
                Note(defect.remedy + " for " + defect.kind);
                return;
            }
        }

        /// <summary>
        /// Withdraws standing orders whose reason has gone away.
        ///
        /// Orders outlive their justification. The case that matters is a colony that was
        /// comfortable when it decided to break up a barracks and is destitute by the time
        /// anyone gets to the job: pulling the beds out is now precisely the wrong move, and
        /// without this the order stands and the colony dismantles the one room everybody is
        /// sleeping in during the crisis that made it poor.
        ///
        /// Nothing else in the director ever cancelled anything it had asked for.
        /// </summary>
        bool CancelStaleOrders(DirectorContext ctx)
        {
            float means = BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists);
            if (!BuildingMeans.Destitute(means)) return false;

            var lister = ctx.map.listerBuildings;
            if (lister == null) return false;

            foreach (var bed in lister.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == null || !bed.Spawned) continue;
                if (!bed.ForColonists || bed.Medical) continue;
                if (!PlacementUtil.CancelDesignation(ctx.map, bed, DesignationDefOf.Uninstall)) continue;

                Chronicle.Record(ChronicleCategory.Build, string.Format(
                    "upkeep — cancelling the order to move a bed out of its room: means have " +
                    "fallen to {0:0.00} and sharing is now the right answer", means));
                Note("cancelled a de-sharing order");
                return true;
            }
            return false;
        }

        string lastReport;

        /// <summary>
        /// A standing account of the colony's condition: what it can afford, what is wrong with
        /// it, and what it is unhappy about that the director has no answer for.
        ///
        /// Recorded on the chronicle rather than behind the test harness, because this is the
        /// line that makes a long unattended run diagnosable — whether upkeep is converging or
        /// oscillating is invisible from the remedies alone. Only written when it changes, or an
        /// established colony would repeat the same sentence four times a day forever.
        /// </summary>
        void Report(DirectorContext ctx, float means, int defectCount)
        {
            // Worst first: this list is read to decide what to teach the director next, and the
            // biggest single penalty is the answer to that question.
            unhandled.Sort(delegate(UnmetComplaint a, UnmetComplaint b)
            {
                return b.mood.CompareTo(a.mood);
            });

            // The same finding, handed to the scorer. The chronicle line is for whoever reads it
            // later; this is what lets the epoch's fitness know the colony spent a fortnight
            // miserable about something nobody had taught the director to fix.
            if (ctx.director != null && ctx.director.accumulator != null)
            {
                float total = 0f;
                for (int i = 0; i < unhandled.Count; i++) total += unhandled[i].mood;

                ctx.director.accumulator.NoteUnmetComplaints(
                    total,
                    unhandled.Count > 0 ? unhandled[0].thought : "",
                    unhandled.Count > 0 ? unhandled[0].mood : 0f);
            }

            string report = string.Format(
                "upkeep — means {0:0.00} ({1} material), {2} defects{3}",
                means, ctx.state.usableMaterial, defectCount,
                unhandled.Count > 0
                    ? "; cannot fix yet: " + string.Join(", ", unhandled.ToArray())
                    : "");

            if (report == lastReport) return;
            lastReport = report;
            Chronicle.Record(ChronicleCategory.Vitals, report);
        }


        bool Apply(DirectorContext ctx, ColonyDefect defect)
        {
            switch (defect.remedy)
            {
                case RemedyKind.RoofOver: return RoofOver(ctx, defect);
                case RemedyKind.Relocate: return Relocate(ctx, defect);
                case RemedyKind.AddLight: return AddLight(ctx, defect);
                case RemedyKind.RemoveSurplusBeds: return RemoveSurplusBeds(ctx, defect);
                case RemedyKind.AddBeauty: return AddBeauty(ctx, defect);
                case RemedyKind.Reclaim: return Reclaim(ctx, defect);
                case RemedyKind.BuryDead: return BuryDead(ctx, defect);
                case RemedyKind.AddHeater: return AddHeater(ctx);
                case RemedyKind.AddCooler: return AddCooler(ctx);
                case RemedyKind.AddTable: return AddTable(ctx);
                case RemedyKind.AddRecreation: return AddRecreation(ctx);
                default: return false;
            }
        }

        // ------------------------------------------------------------ remedies

        /// <summary>
        /// Digs a grave near where the body is.
        ///
        /// A grave needs no research and costs nothing whatsoever, which makes this the best
        /// trade in the game: the single largest mood penalty, removed for free. Colonists haul
        /// their own dead into it once one exists — the director only has to provide the hole.
        /// </summary>
        static bool BuryDead(DirectorContext ctx, ColonyDefect defect)
        {
            var grave = AcDefs.Grave;
            if (grave == null) return false;

            // Digging the grave is not burying anybody.
            //
            // They are two separate actions, and only the first was ever taken: a colony would
            // build a grave, stand next to it with the body still lying where it fell, and go on
            // paying the -10 for an unburied colonist indefinitely. Carrying the corpse to the
            // grave is an ordinary hauling job, but only for a grave whose storage settings
            // accept that corpse and a corpse nobody has forbidden — and almost everything on a
            // RimWorld map arrives forbidden.
            if (EmptyGraveExists(ctx.map))
            {
                return ReleaseTheDeadForBurial(ctx);
            }

            var near = defect.thing != null && defect.thing.Spawned
                ? defect.thing.Position : ctx.Origin;

            foreach (var cell in GenRadial.RadialCellsAround(near, 18, true))
            {
                if (PlacementUtil.TryPlace(ctx.map, grave, cell, Rot4.North, null))
                {
                    PlacementUtil.MarkHome(ctx.map, cell);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Makes an existing empty grave actually usable: the grave willing to take the body,
        /// and the body free to be carried.
        ///
        /// Returns true only when something was changed, so the caller does not report a remedy
        /// on a pass where nothing happened.
        /// </summary>
        static bool ReleaseTheDeadForBurial(DirectorContext ctx)
        {
            var map = ctx.map;
            bool changed = false;

            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i] as Corpse;
                if (corpse == null || !corpse.Spawned) continue;
                if (corpse.InnerPawn == null) continue;

                // Colonists and their friends. Raider bodies are not a mood problem and burying
                // them would spend the graves the colony dug for its own.
                if (corpse.InnerPawn.Faction != Faction.OfPlayer) continue;

                if (corpse.IsForbidden(Faction.OfPlayer))
                {
                    corpse.SetForbidden(false, false);
                    changed = true;
                }
            }

            // And a grave that will accept one. A grave's storage filter can exclude the very
            // corpse it was dug for, in which case the haul job never exists to be taken.
            var graveDef = AcDefs.Grave;
            var graves = graveDef != null ? map.listerThings.ThingsOfDef(graveDef) : null;
            for (int i = 0; graves != null && i < graves.Count; i++)
            {
                var building = graves[i] as Building_Grave;
                if (building == null || building.HasCorpse) continue;

                var settings = building.GetStoreSettings();
                if (settings == null || settings.filter == null) continue;
                if (settings.filter.AllowedDefCount > 0) continue;

                settings.filter.SetAllowAll(null);
                changed = true;
            }

            if (changed)
                Chronicle.Record(ChronicleCategory.Build,
                    "unforbade the dead and opened a grave to them — digging one and burying " +
                    "someone in it are two different jobs, and only the first was ever ordered");

            return changed;
        }

        static bool EmptyGraveExists(Map map)
        {
            var grave = AcDefs.Grave;
            if (grave == null || map.listerThings == null) return false;

            var graves = map.listerThings.ThingsOfDef(grave);
            for (int i = 0; i < graves.Count; i++)
            {
                var building = graves[i] as Building_Grave;
                if (building != null && !building.HasCorpse) return true;
            }

            // One already on order counts, or a grave is queued every pass until it is finished.
            var pending = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < pending.Count; i++)
                if (PlacementUtil.BuildTargetOf(pending[i]) == grave) return true;

            var frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
                if (PlacementUtil.BuildTargetOf(frames[i]) == grave) return true;

            return false;
        }

        /// <summary>
        /// Warmth, by whichever means the colony can actually manage today.
        ///
        /// A heater needs electricity and something generating it — a heater on a dead grid is
        /// as much use as the unpowered turrets that started all of this. But gating warmth on
        /// power alone meant a colony without a generator had *no* answer to cold at all, and
        /// EnvironmentCold and SleptInCold duly sat on the unfixable list at every survey, four
        /// mood each, for the whole of a colony's short life.
        ///
        /// A campfire needs neither power nor research and was already defined here, used only
        /// for cooking. It burns wood and it is a fire in a wooden room, which is a real cost —
        /// but freezing is not the safer option, and cold is measured in dead colonists rather
        /// than in mood once it passes ten degrees below what they can bear.
        /// </summary>
        static bool AddHeater(DirectorContext ctx)
        {
            if (ctx.state.workingGenerators > 0 &&
                PlaceInBase(ctx, AcDefs.Heater, 1, RoomPreference.Coldest)) return true;
            return PlaceInBase(ctx, AcDefs.Campfire, 1, RoomPreference.Coldest);
        }

        /// <summary>
        /// Cooling, by whichever means the colony can manage — and it can manage one without
        /// power, which an earlier version of this comment wrongly denied.
        ///
        /// A passive cooler costs fifty wood, needs no research and needs no electricity. It is
        /// the exact counterpart of the campfire on the cold side, and writing "heat has no
        /// low-technology answer" put EnvironmentHot back on the unfixable list for every colony
        /// without a grid — which is most of them, for most of their lives.
        ///
        /// The electric cooler is better where there is power to run it, so it is tried first;
        /// the passive one is what a pre-electricity colony actually gets.
        /// </summary>
        static bool AddCooler(DirectorContext ctx)
        {
            var cooler = AcDefs.Cooler;
            if (ctx.state.workingGenerators > 0 && cooler != null &&
                PlacementUtil.ResearchDone(cooler) &&
                PlaceInBase(ctx, cooler, 1, RoomPreference.Hottest))
                return true;

            var passive = AcDefs.Thing("PassiveCooler");
            if (passive == null || !PlacementUtil.ResearchDone(passive)) return false;
            if (!PlaceInBase(ctx, passive, 1, RoomPreference.Hottest)) return false;

            Chronicle.Record(ChronicleCategory.Build,
                "passive cooler placed — fifty wood, no research and no grid, which is the " +
                "answer a colony without electricity actually has to heat");
            return true;
        }

        /// <summary>
        /// Something to eat off.
        ///
        /// Placed in whatever room has space rather than waiting for a Dining room. The table
        /// was only ever queued as part of one, and that is a discretionary pick after storage,
        /// beds and a kitchen — so a colony that never got comfortable never got a table, and
        /// paid three mood per colonist at every meal indefinitely.
        /// </summary>
        static bool AddTable(DirectorContext ctx)
        {
            return PlaceInBase(ctx, AcDefs.Thing("Table2x2c"), 1);
        }

        /// <summary>Somewhere to play. Horseshoes needs no research and barely any material.</summary>
        /// <summary>
        /// Candidate joy buildings, easiest to place first.
        ///
        /// A game of Ur needs no research and no clear ground; horseshoes needs a throwing lane
        /// and chess and poker need Complex Furniture. Ordered so the colony that most needs
        /// cheering up — young, unresearched, living in one small room — is served by the first
        /// entry rather than by none of them.
        /// </summary>
        static readonly string[] JoyBuildings =
        {
            "GameOfUrBoard", "ChessTable", "PokerTable", "HorseshoesPin", "BilliardsTable"
        };

        /// <summary>
        /// Something to do that is not work.
        ///
        /// This asked for a horseshoes pin and nothing else, which carries
        /// `PlaceWorker_WatchArea` — it needs a clear lane to throw down. Remedies are placed
        /// inside the planned rooms, and a seven-by-seven room has a five-by-five interior with
        /// beds in it, so every candidate cell was refused. The complaint was therefore attempted
        /// and failed on every pass for the whole of a colony's life: seven times in six-hour
        /// intervals in one run, with `Cheerless` at full severity throughout and the colony
        /// eventually dying at zero mood.
        ///
        /// Chosen on what will actually stand in the space available, not on a def existing —
        /// the same rule this codebase already applies to stoves and generators.
        /// </summary>
        static bool AddRecreation(DirectorContext ctx)
        {
            for (int i = 0; i < JoyBuildings.Length; i++)
            {
                var def = AcDefs.Thing(JoyBuildings[i]);
                if (def == null || !PlacementUtil.ResearchDone(def)) continue;
                if (!PlaceInBase(ctx, def, 1)) continue;

                Chronicle.Record(ChronicleCategory.Build,
                    "recreation: placed a " + (def.label ?? def.defName) +
                    " — the first joy building that would actually fit the space");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Puts one of something into the first planned room with space for it.
        ///
        /// Some complaints belong to no particular room — nobody has anywhere to eat, nobody has
        /// anything to do — so the remedy chooses rather than the survey.
        /// </summary>
        static bool PlaceInBase(DirectorContext ctx, ThingDef def, int count)
        {
            return PlaceInBase(ctx, def, count, RoomPreference.Any);
        }

        enum RoomPreference { Any, Hottest, Coldest }

        /// <summary>
        /// As above, but able to pick the room by its own temperature.
        ///
        /// Temperature is a property of a room, not of the map: one room can be baking while the
        /// one next door is fine, because heat is held by walls and moved by what is inside them.
        /// A cooler dropped into "the first room with space" is therefore as likely to cool a
        /// room nobody was complaining about as the one that drove the complaint, and the colony
        /// pays the wood either way.
        /// </summary>
        static bool PlaceInBase(DirectorContext ctx, ThingDef def, int count, RoomPreference prefer)
        {
            if (def == null || ctx.layout == null) return false;

            var stuff = PlacementUtil.ChooseStuff(ctx.map, def,
                FireRisk.StonePreference(ctx, FireRisk.Assess(ctx.map, ctx.state)));

            var rooms = new List<PlannedRoom>(ctx.layout.rooms);
            if (prefer != RoomPreference.Any)
            {
                var map = ctx.map;
                bool hottestFirst = prefer == RoomPreference.Hottest;
                rooms.Sort(delegate(PlannedRoom a, PlannedRoom b)
                {
                    float ta = RoomTemperature(map, a);
                    float tb = RoomTemperature(map, b);
                    return hottestFirst ? tb.CompareTo(ta) : ta.CompareTo(tb);
                });
            }

            for (int i = 0; i < rooms.Count; i++)
            {
                foreach (var cell in rooms[i].Interior)
                {
                    if ((cell - rooms[i].Door).LengthHorizontalSquared <= 2) continue;
                    if (PlacementUtil.TryPlace(ctx.map, def, cell, Rot4.North, stuff)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A planned room's actual temperature, or the outdoor reading when it has no walls yet.
        /// </summary>
        static float RoomTemperature(Map map, PlannedRoom planned)
        {
            try
            {
                var room = planned.Center.GetRoom(map);
                if (room != null && !room.UsesOutdoorTemperature) return room.Temperature;
                return map.mapTemperature.OutdoorTemp;
            }
            catch (Exception) { return 0f; }
        }

        /// <summary>Roofs every cell the building stands on, which is cheaper than moving it.</summary>
        static bool RoofOver(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.thing == null || !defect.thing.Spawned) return false;

            int marked = 0;
            foreach (var cell in defect.thing.OccupiedRect())
            {
                if (PlacementUtil.TryMarkRoofSupported(ctx.map, cell)) marked++;
                PlacementUtil.MarkHome(ctx.map, cell);
            }
            return marked > 0;
        }

        /// <summary>
        /// Moves the building somewhere it belongs.
        ///
        /// Three routes, cheapest first. Anything minifiable is carried to a sheltered spot
        /// intact, which costs nothing at all — the game has a reinstall job for exactly this.
        /// Failing a spot to put it, it is uninstalled and kept as an item for later. Only
        /// something that cannot be picked up is knocked down, and that is the expensive
        /// option: `resourcesFractionWhenDeconstructed` is per-def and several buildings return
        /// none of their cost.
        /// </summary>
        static bool Relocate(DirectorContext ctx, ColonyDefect defect)
        {
            var thing = defect.thing;
            if (thing == null || !thing.Spawned) return false;
            if (PlacementUtil.AlreadyOrdered(ctx.map, thing)) return false;

            // Never pull down the only thing generating, or the colony loses its grid to fix a
            // risk that has not happened yet.
            if (IsLastWorkingGenerator(ctx, thing)) return false;

            if (PlacementUtil.Movable(thing))
            {
                var shelter = FindShelteredSpot(ctx, thing);
                if (shelter.IsValid &&
                    PlacementUtil.TryReinstall(ctx.map, thing, shelter, thing.Rotation))
                {
                    defect.what += " — moving it under cover at " + shelter;
                    return true;
                }

                // Nowhere to put it yet. Lift it anyway rather than leaving it in the rain; it
                // keeps its quality and every unit of material as an item.
                if (PlacementUtil.TryUninstall(ctx.map, thing))
                {
                    defect.what += " — uninstalling it to place later";
                    return true;
                }
                return false;
            }

            return PlacementUtil.TryDeconstruct(ctx.map, thing);
        }

        /// <summary>A roofed cell inside one of the planner's rooms that will take this thing.</summary>
        static IntVec3 FindShelteredSpot(DirectorContext ctx, Thing thing)
        {
            if (ctx.layout == null) return IntVec3.Invalid;

            var rooms = ctx.layout.rooms;
            for (int i = 0; i < rooms.Count; i++)
            {
                foreach (var cell in rooms[i].Interior)
                {
                    if (!cell.InBounds(ctx.map)) continue;
                    if (ctx.map.roofGrid == null || !ctx.map.roofGrid.Roofed(cell)) continue;
                    if (PlacementUtil.HasAnyConstructionAt(ctx.map, cell)) continue;

                    var report = GenConstruct.CanPlaceBlueprintAt(thing.def, cell, thing.Rotation,
                                                                  ctx.map, false, thing, thing);
                    if (report.Accepted) return cell;
                }
            }
            return IntVec3.Invalid;
        }

        static bool IsLastWorkingGenerator(DirectorContext ctx, Thing thing)
        {
            var trader = thing.TryGetComp<CompPowerTrader>();
            if (trader == null || trader.Props == null) return false;
            if (trader.Props.PowerConsumption >= 0f) return false;

            return ctx.state.workingGenerators <= 1;
        }

        static bool AddLight(DirectorContext ctx, ColonyDefect defect)
        {
            var lamp = AcDefs.Torch;
            if (lamp == null || defect.room == null) return false;

            var stuff = PlacementUtil.ChooseStuff(ctx.map, lamp,
                FireRisk.StonePreference(ctx, FireRisk.Assess(ctx.map, ctx.state)));

            foreach (var cell in defect.room.Cells)
            {
                if (PlacementUtil.TryPlace(ctx.map, lamp, cell, Rot4.North, stuff)) return true;
            }
            return false;
        }

        /// <summary>
        /// Leaves one bed and takes the rest out, turning a barracks back into a bedroom.
        ///
        /// The planner then wants beds it no longer has and reserves another room for them,
        /// which is the outcome worth having: an awful barracks is -7 mood against an awful
        /// bedroom's -2, so the room count is what matters, not the decoration.
        /// </summary>
        static bool RemoveSurplusBeds(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.room == null) return false;

            var beds = new List<Building_Bed>();
            var things = defect.room.ContainedAndAdjacentThings;
            for (int i = 0; i < things.Count; i++)
            {
                var bed = things[i] as Building_Bed;
                if (bed == null || !bed.Spawned) continue;
                if (bed.GetRoom() != defect.room) continue;
                if (bed.ForColonists && !bed.Medical) beds.Add(bed);
            }

            if (beds.Count <= 1) return false;

            // Uninstalled, not deconstructed. The colony wants this bed — just not here — and
            // uninstalling keeps it whole, quality included, ready to be set down in the room
            // the planner is about to reserve. Knocking it down would return a fraction of the
            // material and none of the workmanship.
            //
            // One at a time, so the colony is never left with nowhere to sleep while the
            // replacement rooms are still going up.
            for (int i = 1; i < beds.Count; i++)
            {
                if (PlacementUtil.MarkedForDeconstruction(ctx.map, beds[i])) continue;
                if (PlacementUtil.TryUninstall(ctx.map, beds[i])) return true;
            }
            return false;
        }

        /// <summary>
        /// Takes a surplus room apart and gets its material back.
        ///
        /// The room leaves the layout at the same time, which is what stops this from being a
        /// loop: the planner counts rooms it has reserved, so a room deconstructed but still on
        /// the books would simply be rebuilt, and the colony would saw away at itself forever.
        ///
        /// Everything goes — walls, door and whatever was inside — because the point is the
        /// material, and furniture standing in an unwalled square is just something else to
        /// deteriorate.
        /// </summary>
        static bool Reclaim(DirectorContext ctx, ColonyDefect defect)
        {
            var planned = defect.plannedRoom;
            if (planned == null || ctx.layout == null) return false;

            int marked = 0;
            foreach (var cell in planned.Rect)
            {
                if (!cell.InBounds(ctx.map)) continue;

                // Neighbouring rooms share a wall by design — the layout budges them together
                // to keep the base cheap. Pulling one down cell by cell would therefore breach
                // the room next door and leave it open to the sky, which is a far worse problem
                // than the one being solved.
                if (SharedWithAnotherRoom(ctx.layout, planned, cell)) continue;

                // Anything still only ordered is withdrawn rather than built and then knocked
                // down again. Finishing a wall in order to demolish it spends the material twice
                // over, which is the exact opposite of what reclaiming is for.
                marked += PlacementUtil.CancelConstructionAt(ctx.map, cell);

                var things = cell.GetThingList(ctx.map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    var thing = things[i];
                    if (thing == null || thing.Faction != Faction.OfPlayer) continue;
                    if (thing.def == null || thing.def.category != ThingCategory.Building) continue;

                    // Furniture comes up whole and keeps its quality; only the shell is knocked
                    // down, because walls cannot be carried.
                    if (PlacementUtil.Movable(thing))
                    {
                        if (PlacementUtil.TryUninstall(ctx.map, thing)) marked++;
                        continue;
                    }

                    if (PlacementUtil.TryDeconstruct(ctx.map, thing)) marked++;
                }
            }

            if (marked == 0) return false;

            // Off the books, so the planner treats the slot as gone rather than as a room it
            // still owns and ought to finish.
            ctx.layout.rooms.Remove(planned);
            return true;
        }

        /// <summary>True when a cell also belongs to some other room the colony is keeping.</summary>
        static bool SharedWithAnotherRoom(BaseLayout layout, PlannedRoom keeping, IntVec3 cell)
        {
            if (layout == null) return false;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var other = layout.rooms[i];
                if (other == keeping) continue;
                if (other.Rect.Contains(cell)) return true;
            }
            return false;
        }

        /// <summary>
        /// Something worth looking at. A lamp first if the room somehow still has none, since it
        /// is both beauty and light; otherwise a plant pot, which is the cheapest beauty in the
        /// game that does not need a skilled crafter.
        /// </summary>
        static bool AddBeauty(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.room == null) return false;

            if (!DefectSurvey.HasLight(ctx.map, defect.room) && AddLight(ctx, defect)) return true;

            var pot = AcDefs.Thing("PlantPot");
            if (pot == null) return false;

            var stuff = PlacementUtil.ChooseStuff(ctx.map, pot, 0.5f);
            foreach (var cell in defect.room.Cells)
            {
                if (PlacementUtil.TryPlace(ctx.map, pot, cell, Rot4.North, stuff)) return true;
            }
            return false;
        }
    }
}
