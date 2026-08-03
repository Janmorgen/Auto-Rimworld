using System;
using RimWorld;
using Verse;

namespace AutoColony.Rooms
{
    /// <summary>
    /// Counts how much of the standing base is up to the standard its roles ask for.
    ///
    /// The finish-time verdict in the planner says how one room turned out, once, and then the
    /// colony lives in it for the rest of its life. This is the same judgement asked repeatedly
    /// of everything standing, which is what the score needs: a term built on finish events
    /// would go quiet the moment a colony stopped building, and a colony coasting in a base of
    /// cramped huts is exactly the case worth scoring.
    ///
    /// Measured later than the finish check for a second reason. A room's impressiveness is not
    /// settled when its walls close — the furniture is still blueprints at that point — so the
    /// verdict taken at finish is honest about the shell and premature about everything else.
    /// Sampling as the colony runs reads the room people are actually living in.
    /// </summary>
    public static class RoomCensus
    {
        /// <summary>
        /// How many planned rooms the game can rate, and how many of those meet their role.
        ///
        /// Rooms still being built are skipped rather than counted against: an unfinished room
        /// is not a badly built one, and punishing it would score construction speed twice.
        /// </summary>
        public static void Take(Map map, BaseLayout layout, out int judged, out int upToStandard)
        {
            judged = 0;
            upToStandard = 0;
            if (map == null || layout == null) return;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var planned = layout.rooms[i];
                try
                {
                    var room = planned.Center.GetRoom(map);
                    if (room == null || room.TouchesMapEdge || room.PsychologicallyOutdoors) continue;

                    // Space as the shell scored it, impressiveness as it stands now.
                    //
                    // The two are not alike. Impressiveness genuinely changes as a room is
                    // furnished and kept, so the live reading is the true one. Space does not —
                    // the walls are where they were put — but its *measurement* drops as
                    // furniture goes in, because cells under an impassable building leave the
                    // room's region. Judged live, a kitchen is marked down for owning a stove
                    // and a workshop for owning its benches, which is the opposite of what this
                    // term is for. Measured in run 53: a 7x7 kitchen rated average-sized when
                    // its walls closed and rather tight once the stove was in.
                    int space = planned.shellSpaceStage >= 0
                        ? planned.shellSpaceStage
                        : Stage(RoomStatDefOf.Space, room.GetStat(RoomStatDefOf.Space));
                    int impressiveness = Stage(RoomStatDefOf.Impressiveness,
                                               room.GetStat(RoomStatDefOf.Impressiveness));

                    judged++;
                    if (RoomQuality.Shortfall(planned.role.ToString(), space, "", impressiveness,
                                              "", Upkeep.DefectSurvey.ColonistBedsIn(room))
                        == null) upToStandard++;
                }
                catch (Exception) { }
            }
        }

        static int Stage(RoomStatDef stat, float score)
        {
            return stat == null ? 0 : stat.GetScoreStageIndex(score);
        }
    }
}
