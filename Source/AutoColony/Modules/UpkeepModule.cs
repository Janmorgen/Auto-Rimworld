using System.Collections.Generic;
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

        readonly List<string> unhandled = new List<string>();

        protected override void Act(DirectorContext ctx)
        {
            // Nothing discretionary while the colony is burning or being shot at.
            if (ctx.state.EmergencyAtHome) return;
            if (ctx.plan != null && ctx.plan.EmergencyActive) return;

            // Withdraw anything the colony asked for and no longer wants, before asking for more.
            if (CancelStaleOrders(ctx)) return;

            unhandled.Clear();
            var defects = DefectSurvey.Survey(ctx.map, ctx.state, ctx.layout, unhandled);

            Report(ctx, BuildingMeans.Assess(ctx.state.usableMaterial, ctx.state.colonists),
                   defects.Count);
            if (defects.Count == 0) return;

            for (int i = 0; i < defects.Count; i++)
            {
                var defect = defects[i];
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
            unhandled.Sort();

            // Hand the same finding to the scorer. The chronicle line is for whoever reads it
            // later; this is what lets the epoch's fitness know the colony spent a fortnight
            // miserable about something nobody had taught the director to fix.
            if (ctx.director != null && ctx.director.accumulator != null)
            {
                float total = 0f, worstMood = 0f;
                string worst = "";
                for (int i = 0; i < unhandled.Count; i++)
                {
                    float mood = MoodOf(unhandled[i]);
                    total += mood;
                    if (mood > worstMood) { worstMood = mood; worst = NameOf(unhandled[i]); }
                }
                ctx.director.accumulator.NoteUnmetComplaints(total, worst, worstMood);
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

        /// <summary>
        /// The mood cost out of an entry like "EnvironmentCold (-4.0)". The survey formats these
        /// for a reader; parsing them back is cheap and keeps one list serving both purposes.
        /// </summary>
        static float MoodOf(string entry)
        {
            int open = entry.LastIndexOf('(');
            int close = entry.LastIndexOf(')');
            if (open < 0 || close <= open) return 0f;

            float mood;
            if (!float.TryParse(entry.Substring(open + 1, close - open - 1), out mood)) return 0f;
            return mood < 0f ? -mood : mood;
        }

        static string NameOf(string entry)
        {
            int open = entry.LastIndexOf('(');
            return open > 0 ? entry.Substring(0, open).Trim() : entry;
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
                default: return false;
            }
        }

        // ------------------------------------------------------------ remedies

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
