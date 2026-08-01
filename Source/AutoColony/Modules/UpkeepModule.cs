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

            unhandled.Clear();
            var defects = DefectSurvey.Survey(ctx.map, ctx.state, ctx.layout, unhandled);

            ReportUnhandled();
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
        /// Says what the colony is unhappy about that the director has no answer for.
        ///
        /// Without this the unfixable complaints vanish silently, and the next person deciding
        /// what to teach it is guessing. With it, the chronicle names them.
        /// </summary>
        void ReportUnhandled()
        {
            if (unhandled.Count == 0) return;

            unhandled.Sort();
            Chronicle.Record(ChronicleCategory.Vitals,
                "unhappy about things the director cannot fix yet: " +
                string.Join(", ", unhandled.ToArray()));
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
        /// Takes the building down. The planner's own repair path then notices its room is
        /// missing its key furniture and re-places it inside, which is what makes this a move
        /// rather than a demolition.
        /// </summary>
        static bool Relocate(DirectorContext ctx, ColonyDefect defect)
        {
            if (defect.thing == null || !defect.thing.Spawned) return false;
            if (PlacementUtil.MarkedForDeconstruction(ctx.map, defect.thing)) return false;

            // Never pull down the only thing generating, or the colony loses its grid to fix a
            // risk that has not happened yet.
            if (IsLastWorkingGenerator(ctx, defect.thing)) return false;

            return PlacementUtil.TryDeconstruct(ctx.map, defect.thing);
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

            // One at a time, so the colony is never left with nowhere to sleep while the
            // replacement rooms are still going up.
            for (int i = 1; i < beds.Count; i++)
            {
                if (PlacementUtil.MarkedForDeconstruction(ctx.map, beds[i])) continue;
                if (PlacementUtil.TryDeconstruct(ctx.map, beds[i])) return true;
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

                var things = cell.GetThingList(ctx.map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    var thing = things[i];
                    if (thing == null || thing.Faction != Faction.OfPlayer) continue;
                    if (thing.def == null || thing.def.category != ThingCategory.Building) continue;

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
