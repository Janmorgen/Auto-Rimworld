using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Watches the player run the colony and records what their choices imply about a strategy.
    ///
    /// This is the only module that runs while automation is switched off — that is the point.
    /// It never touches the game; it only reads. The resulting <see cref="PlayerModel"/> is
    /// used as the starting incumbent when the player eventually hands the colony over, which
    /// saves the search from spending its scarce epochs rediscovering the obvious.
    /// </summary>
    public class PlayerObservationModule : DirectorModule
    {
        public override string Name { get { return "Learn from player"; } }

        /// <summary>One observation per in-game hour.</summary>
        public override int IntervalTicks { get { return 2500; } }

        protected override void Act(DirectorContext ctx)
        {
            var model = ctx.director.playerModel;
            if (model == null) return;

            var s = ctx.state;
            int colonists = s.colonists > 0 ? s.colonists : 1;

            model.samples++;

            model.foodDaysSum += s.daysOfFood;
            model.woodSum += s.wood;
            model.steelSum += s.steel;
            model.componentsSum += s.components;
            model.textilesSum += s.textiles;
            model.medicinePerColonistSum += s.medicineCount / (float)colonists;

            ObserveWork(ctx, model);
            ObserveZones(ctx, model, colonists);
            ObserveRooms(ctx, model);
            ObservePolicy(ctx, model);
            ObserveChoices(ctx, model);

            if (model.samples % 50 == 0)
                Note("observed the player for " + model.samples + " samples");
        }

        /// <summary>
        /// Emphasis per work type: how many capable colonists are on it, and how urgently.
        /// Priority 1 counts four times as strongly as priority 4, and unassigned counts zero.
        /// </summary>
        void ObserveWork(DirectorContext ctx, PlayerModel model)
        {
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            var pawns = ctx.state.allColonists;
            if (pawns.Count == 0) return;

            for (int w = 0; w < workTypes.Count; w++)
            {
                var wt = workTypes[w];
                if (wt == null || !wt.visible) continue;

                int capable = 0;
                float urgency = 0f;

                for (int i = 0; i < pawns.Count; i++)
                {
                    var pawn = pawns[i];
                    if (pawn.workSettings == null || !pawn.workSettings.Initialized) continue;
                    if (pawn.WorkTypeIsDisabled(wt)) continue;

                    capable++;
                    int priority = pawn.workSettings.GetPriority(wt);
                    if (priority > 0) urgency += (5 - priority) / 4f;
                }

                if (capable == 0) continue;
                model.AddWorkEmphasis(wt.defName, urgency / capable);
            }
        }

        void ObserveZones(DirectorContext ctx, PlayerModel model, int colonists)
        {
            int growCells = 0, stockCells = 0;

            foreach (var zone in ctx.map.zoneManager.AllZones)
            {
                if (zone is Zone_Growing) growCells += zone.Cells.Count;
                else if (zone is Zone_Stockpile) stockCells += zone.Cells.Count;
            }

            model.growCellsPerColonistSum += growCells / (float)colonists;
            model.stockCellsPerColonistSum += stockCells / (float)colonists;
        }

        /// <summary>Reads the player's actual bedroom geometry rather than guessing at it.</summary>
        void ObserveRooms(DirectorContext ctx, PlayerModel model)
        {
            var lister = ctx.map.listerBuildings;
            if (lister == null) return;

            var bedsPerRoom = new Dictionary<Room, int>();
            var roomSizes = new Dictionary<Room, int>();

            foreach (var bed in lister.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed == null || !bed.ForColonists || bed.Medical) continue;

                var room = RegionAndRoomQuery.GetRoom(bed);
                if (room == null || room.CellCount <= 0 || room.CellCount > 400) continue;

                int count;
                bedsPerRoom.TryGetValue(room, out count);
                bedsPerRoom[room] = count + 1;
                roomSizes[room] = room.CellCount;
            }

            if (bedsPerRoom.Count == 0) return;

            float bedsTotal = 0f, sizeTotal = 0f;
            foreach (var kv in bedsPerRoom) bedsTotal += kv.Value;
            foreach (var kv in roomSizes)
            {
                // Genes describe a square room's outer edge; rooms report interior area.
                sizeTotal += AcMath.Sqrt(kv.Value) + 2f;
            }

            model.bedsPerRoomSum += bedsTotal / bedsPerRoom.Count;
            model.roomSizeSum += sizeTotal / roomSizes.Count;
            model.roomSamples++;
        }


        void ObservePolicy(DirectorContext ctx, PlayerModel model)
        {
            var pawns = ctx.state.allColonists;
            if (pawns.Count == 0) return;

            float care = 0f, selfTend = 0f;
            int counted = 0;

            for (int i = 0; i < pawns.Count; i++)
            {
                var ps = pawns[i].playerSettings;
                if (ps == null) continue;
                care += (int)ps.medCare;
                selfTend += ps.selfTend ? 1f : 0f;
                counted++;
            }

            if (counted == 0) return;
            model.medCareSum += care / counted;
            model.selfTendSum += selfTend / counted;

            var prisoners = ctx.map.mapPawns.PrisonersOfColony;
            if (prisoners.Count > 0)
            {
                int recruiting = 0;
                for (int i = 0; i < prisoners.Count; i++)
                {
                    var guest = prisoners[i].guest;
                    if (guest != null && guest.ExclusiveInteractionMode == PrisonerInteractionModeDefOf.AttemptRecruit)
                        recruiting++;
                }
                model.recruitSum += recruiting / (float)prisoners.Count;
                model.recruitSamples++;
            }
        }

        /// <summary>Records the discrete preferences the bandits also choose between.</summary>
        void ObserveChoices(DirectorContext ctx, PlayerModel model)
        {
            foreach (var zone in ctx.map.zoneManager.AllZones)
            {
                var grow = zone as Zone_Growing;
                if (grow == null) continue;
                var plant = grow.GetPlantDefToGrow();
                if (plant != null) model.CountCrop(plant.defName);
            }

            var rm = Find.ResearchManager;
            if (rm != null)
            {
                var project = rm.GetProject();
                if (project != null && !project.IsFinished) model.CountResearch(project.defName);
            }
        }
    }
}
