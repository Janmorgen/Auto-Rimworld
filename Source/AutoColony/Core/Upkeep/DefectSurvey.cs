using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony.Upkeep
{
    /// <summary>One concrete thing that is wrong, and what it is wrong with.</summary>
    public class ColonyDefect
    {
        public DefectKind kind;
        public RemedyKind remedy;
        public float severity;

        /// <summary>The offending building, when the defect is about a thing.</summary>
        public Thing thing;

        /// <summary>The offending room, when the defect is about a place.</summary>
        public Room room;

        /// <summary>The planner's own record of the room, when the remedy has to remove it.</summary>
        public PlannedRoom plannedRoom;

        /// <summary>Where to act, which is the thing's cell or somewhere inside the room.</summary>
        public IntVec3 cell;

        /// <summary>What to say about it in the chronicle.</summary>
        public string what = "";

        public float Priority { get { return DefectPolicy.Priority(kind, severity); } }
    }

    /// <summary>
    /// Walks the built colony looking for things that exist but are wrong.
    ///
    /// Two sources, and they answer different questions. The colonists' own thoughts say what is
    /// costing mood — the colony responding to its measured experience rather than to a rule.
    /// Direct inspection catches what nobody complains about because it has not hurt yet: an
    /// unroofed generator costs nothing at all until the first time it rains.
    ///
    /// Everything it returns names a specific target. That is the point: a count of fourteen
    /// exposed electrical buildings tells the director the colony is at risk but gives it
    /// nothing to point at, so no module could have acted on it however much it wanted to.
    /// </summary>
    public static class DefectSurvey
    {
        /// <summary>Below this psychological glow, a room reads as dark to a colonist.</summary>
        const float DarkGlow = 0.3f;

        static readonly List<Thought> thoughtBuffer = new List<Thought>();

        public static List<ColonyDefect> Survey(Map map, ColonyState state, BaseLayout layout,
                                                List<string> unhandled)
        {
            var defects = new List<ColonyDefect>();
            if (map == null || state == null) return defects;

            var complaints = GatherComplaints(state, unhandled);
            float means = BuildingMeans.Assess(state.usableMaterial, state.colonists);

            SurveyExposedPower(map, state, defects);
            SurveyRooms(map, state, means, complaints, defects);
            SurveyOverbuilding(map, state, layout, means, defects);

            defects.Sort(delegate(ColonyDefect a, ColonyDefect b)
            {
                return b.Priority.CompareTo(a.Priority);
            });
            return defects;
        }

        // ------------------------------------------------------------ what colonists say

        /// <summary>
        /// The mood cost of each complaint the colony is currently carrying, summed across
        /// everyone feeling it, keyed by defect. Complaints with no remedy are recorded in
        /// <paramref name="unhandled"/> rather than dropped, because the ones the director
        /// cannot yet fix are exactly the list worth reading before deciding what to build next.
        /// </summary>
        static Dictionary<DefectKind, float> GatherComplaints(ColonyState state, List<string> unhandled)
        {
            var worst = new Dictionary<DefectKind, float>();

            for (int i = 0; i < state.allColonists.Count; i++)
            {
                var pawn = state.allColonists[i];
                if (pawn == null || pawn.needs == null || pawn.needs.mood == null) continue;
                var thoughts = pawn.needs.mood.thoughts;
                if (thoughts == null) continue;

                thoughtBuffer.Clear();
                thoughts.GetDistinctMoodThoughtGroups(thoughtBuffer);

                for (int t = 0; t < thoughtBuffer.Count; t++)
                {
                    var group = thoughtBuffer[t];
                    if (group == null || group.def == null) continue;

                    float offset = thoughts.MoodOffsetOfGroup(group);
                    if (offset >= 0f) continue;

                    float severity = Complaints.Severity(offset);
                    DefectKind kind;
                    if (!Complaints.TryMap(group.def.defName, out kind))
                    {
                        if (unhandled != null && severity >= 0.2f)
                            unhandled.Add(group.def.defName + " (" + offset.ToString("0.0") + ")");
                        continue;
                    }

                    float current;
                    worst.TryGetValue(kind, out current);
                    if (severity > current) worst[kind] = severity;
                }
            }

            return worst;
        }

        // ------------------------------------------------------------ electrical exposure

        /// <summary>
        /// Electrical buildings standing under open sky, one defect each.
        ///
        /// Conduits are skipped deliberately. They dominate the count — a single run across open
        /// ground is a dozen of them — but they are a routing problem, not a placement one:
        /// tearing one out just breaks the grid, and roofing open ground away from a wall is not
        /// possible. Devices are the ones worth acting on, and they are the ones a colony
        /// actually loses when a fire starts.
        /// </summary>
        static void SurveyExposedPower(Map map, ColonyState state, List<ColonyDefect> defects)
        {
            var exposed = state.exposedPoweredDevices;
            for (int i = 0; i < exposed.Count; i++)
            {
                var thing = exposed[i];
                if (thing == null || !thing.Spawned) continue;

                // Already on its way out. Still reporting it would leave the survey permanently
                // showing work that is in hand, and hide whatever is behind it in the queue.
                if (PlacementUtil.MarkedForDeconstruction(map, thing)) continue;

                // Roofing beats moving whenever the spot can hold a roof at all. Open ground far
                // from any wall cannot, and there the only honest answer is to take it down.
                bool roofable = RoofCollapseUtility.WithinRangeOfRoofHolder(thing.Position, map, false);

                var defect = new ColonyDefect();
                defect.kind = DefectKind.ExposedPowered;
                defect.remedy = roofable ? RemedyKind.RoofOver : RemedyKind.Relocate;
                defect.thing = thing;
                defect.cell = thing.Position;
                defect.what = thing.LabelCap + " at " + thing.Position +
                              (roofable ? " (roofable)" : " (no roof support — must move)");

                // Rain is when this costs anything, so that is what sets the urgency. A dry
                // spell is the right time to fix it, not the right time to ignore it.
                float rain = map.weatherManager != null ? map.weatherManager.RainRate : 0f;
                defect.severity = 0.35f + rain * 0.65f;

                defects.Add(defect);
            }
        }

        // ------------------------------------------------------------ rooms

        static void SurveyRooms(Map map, ColonyState state, float means,
                                Dictionary<DefectKind, float> complaints, List<ColonyDefect> defects)
        {
            // Only rooms colonists actually sleep in. A dark storeroom bothers nobody.
            var seen = new HashSet<Room>();

            for (int i = 0; i < state.allColonists.Count; i++)
            {
                var pawn = state.allColonists[i];
                if (pawn == null || !pawn.Spawned) continue;

                var bed = pawn.ownership != null ? pawn.ownership.OwnedBed : null;
                var room = bed != null && bed.Spawned ? bed.GetRoom() : null;
                if (room == null || room.PsychologicallyOutdoors || !seen.Add(room)) continue;

                InspectBedroom(map, room, means, complaints, defects);
            }
        }

        static void InspectBedroom(Map map, Room room, float means,
                                   Dictionary<DefectKind, float> complaints, List<ColonyDefect> defects)
        {
            var centre = CentreOf(room);

            // Darkness. The complaint confirms it is being felt, but the glow is what says which
            // room — a colonist carries "EnvironmentDark" around with them and it names nowhere.
            if (IsDark(map, room) && !HasLight(map, room))
            {
                float severity;
                if (!complaints.TryGetValue(DefectKind.DarkRoom, out severity)) severity = 0.35f;

                var defect = new ColonyDefect();
                defect.kind = DefectKind.DarkRoom;
                defect.remedy = RemedyKind.AddLight;
                defect.room = room;
                defect.cell = centre;
                defect.severity = severity;
                defect.what = "unlit " + RoomLabel(room) + " at " + centre;
                defects.Add(defect);
            }

            // A barracks — but only a fault if the colony could afford to separate them. Sharing
            // is the correct answer for a colony with no material, and pulling beds out of the
            // one warm room it has would be the opposite of help.
            int beds = ColonistBedsIn(room);
            if (beds > 1)
            {
                float moodSeverity;
                if (!complaints.TryGetValue(DefectKind.SharedBedroom, out moodSeverity))
                    moodSeverity = 0.4f;   // the thought only lands after someone sleeps there

                float severity = BuildingMeans.SharingSeverity(means, moodSeverity);
                if (severity > 0f)
                {
                    var defect = new ColonyDefect();
                    defect.kind = DefectKind.SharedBedroom;
                    defect.remedy = RemedyKind.RemoveSurplusBeds;
                    defect.room = room;
                    defect.cell = centre;
                    defect.severity = severity;
                    defect.what = beds + " beds sharing " + RoomLabel(room) +
                                  " and the colony can afford to separate them (means " +
                                  means.ToString("0.00") + ")";
                    defects.Add(defect);
                }
            }

            // Dreariness, only once somebody has actually complained. Impressiveness alone is a
            // poor trigger: a brand new room scores badly and nobody has slept in it yet.
            float dreary;
            if (complaints.TryGetValue(DefectKind.DrearyRoom, out dreary) && beds <= 1)
            {
                var defect = new ColonyDefect();
                defect.kind = DefectKind.DrearyRoom;
                defect.remedy = RemedyKind.AddBeauty;
                defect.room = room;
                defect.cell = centre;
                defect.severity = dreary;
                defect.what = RoomLabel(room) + " scores " +
                              room.GetStat(RoomStatDefOf.Impressiveness).ToString("0") +
                              " for impressiveness";
                defects.Add(defect);
            }
        }

        // ------------------------------------------------------------ living beyond its means

        /// <summary>
        /// Rooms the colony built when it was better off and can no longer justify keeping.
        ///
        /// The walls are the stockpile. A seven-cell room holds something like a hundred and
        /// twenty units of material, and deconstruction returns most of it — so a colony that
        /// spread out and then lost its miners is not actually out of resources, it is standing
        /// inside them. Consolidating everybody into one room and taking the rest down is the
        /// move, and it is the only way back out of that hole.
        ///
        /// Only rooms nobody sleeps in are offered up, and never the last of anything: the
        /// kitchen, the store and one bedroom stay whatever happens.
        /// </summary>
        static void SurveyOverbuilding(Map map, ColonyState state, BaseLayout layout, float means,
                                       List<ColonyDefect> defects)
        {
            if (layout == null || !BuildingMeans.Destitute(means)) return;

            var surplus = new List<PlannedRoom>();
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var planned = layout.rooms[i];
                if (!Expendable(map, layout, planned)) continue;
                surplus.Add(planned);
            }

            float severity = BuildingMeans.ReclaimSeverity(means, surplus.Count);
            if (severity <= 0f || surplus.Count == 0) return;

            var target = surplus[0];
            var defect = new ColonyDefect();
            defect.kind = DefectKind.Overbuilt;
            defect.remedy = RemedyKind.Reclaim;
            defect.cell = target.Center;
            defect.severity = severity;
            defect.plannedRoom = target;
            defect.what = "reclaiming the " + target.role + " room at " + target.Center +
                          " — means " + means.ToString("0.00") + " with " + surplus.Count +
                          " rooms the colony no longer needs standing in material it does";
            defects.Add(defect);
        }

        /// <summary>
        /// Whether a planned room can be taken down without costing the colony something it
        /// cannot do without.
        /// </summary>
        static bool Expendable(Map map, BaseLayout layout, PlannedRoom planned)
        {
            // Never the last of a role. One kitchen, one store and one bedroom are the floor.
            if (layout.CountRooms(planned.role) <= 1 &&
                (planned.role == RoomRole.Kitchen || planned.role == RoomRole.Storage ||
                 planned.role == RoomRole.Bedroom || planned.role == RoomRole.Power))
                return false;

            // Nothing anybody is sleeping in or being treated in.
            foreach (var cell in planned.Rect)
            {
                if (!cell.InBounds(map)) continue;
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var bed = things[i] as Building_Bed;
                    if (bed == null) continue;
                    if (bed.OwnersForReading != null && bed.OwnersForReading.Count > 0) return false;
                    if (bed.Medical) return false;
                }
            }
            return true;
        }

        // ------------------------------------------------------------ helpers

        public static bool IsDark(Map map, Room room)
        {
            if (map.glowGrid == null) return false;
            foreach (var cell in room.Cells)
            {
                if (map.glowGrid.GroundGlowAt(cell) >= DarkGlow) return false;
            }
            return true;
        }

        public static bool HasLight(Map map, Room room)
        {
            foreach (var cell in room.Cells)
            {
                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    var thing = things[i];
                    if (thing == null) continue;
                    if (thing.TryGetComp<CompGlower>() != null) return true;

                    // A lamp already on its way counts, or the room is re-lit every pass.
                    var blueprint = thing as Blueprint;
                    if (blueprint != null && blueprint.def.entityDefToBuild is ThingDef &&
                        ((ThingDef)blueprint.def.entityDefToBuild).HasComp(typeof(CompGlower))) return true;
                    var frame = thing as Frame;
                    if (frame != null && frame.def.entityDefToBuild is ThingDef &&
                        ((ThingDef)frame.def.entityDefToBuild).HasComp(typeof(CompGlower))) return true;
                }
            }
            return false;
        }

        public static int ColonistBedsIn(Room room)
        {
            int n = 0;
            var things = room.ContainedAndAdjacentThings;
            for (int i = 0; i < things.Count; i++)
            {
                var bed = things[i] as Building_Bed;
                if (bed == null || !bed.Spawned) continue;
                if (bed.GetRoom() != room) continue;
                if (bed.ForColonists && !bed.Medical) n++;
            }
            return n;
        }

        static string RoomLabel(Room room)
        {
            return room.Role != null ? room.Role.label : "room";
        }

        static IntVec3 CentreOf(Room room)
        {
            int n = 0, x = 0, z = 0;
            foreach (var cell in room.Cells)
            {
                x += cell.x; z += cell.z; n++;
            }
            return n > 0 ? new IntVec3(x / n, 0, z / n) : IntVec3.Invalid;
        }
    }
}
