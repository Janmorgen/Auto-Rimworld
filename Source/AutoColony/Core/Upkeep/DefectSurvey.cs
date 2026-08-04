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

        /// <summary>
        /// How much the room this defect is in matters, 1 when it belongs to no room. Set by the
        /// survey, which is the only thing that knows the base's shape.
        /// </summary>
        public float roomImportance = 1f;

        /// <summary>
        /// How much this kind of fault is worth to *this* colony, taken from its genome. Left at
        /// the built-in default when nobody supplied one, so the survey still works standalone.
        /// </summary>
        public float kindWeight = -1f;

        public float Priority
        {
            get
            {
                return kindWeight >= 0f
                    ? DefectPolicy.Priority(kind, severity, roomImportance, kindWeight)
                    : DefectPolicy.Priority(kind, severity, roomImportance);
            }
        }
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
                                                List<UnmetComplaint> unhandled)
        {
            return Survey(map, state, layout, unhandled, 0.8f, 0.6f, null);
        }

        /// <summary>
        /// As above, weighting each room's faults by how much that room matters.
        ///
        /// The weights come from the genome, so how much to favour a room the colony depends on
        /// over a room that is merely busy is learned across epochs rather than asserted here.
        /// </summary>
        public static List<ColonyDefect> Survey(Map map, ColonyState state, BaseLayout layout,
                                                List<UnmetComplaint> unhandled,
                                                float essentialWeight, float occupancyWeight,
                                                float[] kindWeights,
                                                HashSet<RoomRole> rolesWanted = null)
        {
            var defects = new List<ColonyDefect>();
            if (map == null || state == null) return defects;

            var complaints = GatherComplaints(state, unhandled);
            float means = BuildingMeans.Assess(state.usableMaterial, state.colonists);

            SurveyExposedPower(map, state, defects);
            SurveyDead(map, complaints, defects);
            SurveyComforts(complaints, defects);
            SurveyRooms(map, state, means, complaints, defects, essentialWeight, occupancyWeight);

            // What each kind of fault is worth to this colony, rather than to colonies in
            // general. Supplied from the genome; absent, every defect keeps the built-in default.
            if (kindWeights != null)
            {
                for (int i = 0; i < defects.Count; i++)
                {
                    int k = (int)defects[i].kind;
                    if (k >= 0 && k < kindWeights.Length) defects[i].kindWeight = kindWeights[k];
                }
            }
            SurveyOverbuilding(map, state, layout, means, defects, rolesWanted);

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
        static Dictionary<DefectKind, float> GatherComplaints(ColonyState state, List<UnmetComplaint> unhandled)
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
                        if (unhandled != null && severity >= Complaints.ReportableSeverity)
                            unhandled.Add(new UnmetComplaint(group.def.defName, offset));
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

                // Already dealt with — knocked down, lifted, or being carried somewhere else.
                // Still reporting it would leave the survey permanently showing work that is in
                // hand, and hide whatever is behind it in the queue.
                if (PlacementUtil.AlreadyOrdered(map, thing)) continue;

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

        // ------------------------------------------------------------ the dead

        /// <summary>
        /// Colonists lying where they fell.
        ///
        /// The largest single mood penalty in the game and the cheapest to answer — a grave
        /// needs no research and costs nothing at all. A colony carried two of these for eleven
        /// days and died of the accumulation while every outcome figure still looked survivable.
        /// </summary>
        static void SurveyDead(Map map, Dictionary<DefectKind, float> complaints,
                               List<ColonyDefect> defects)
        {
            if (map.listerThings == null) return;

            Corpse unburied = null;
            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                var corpse = corpses[i] as Corpse;
                if (corpse == null || !corpse.Spawned) continue;

                // Only our own dead carry the penalty, and only while nothing holds them.
                if (corpse.InnerPawn == null || !corpse.InnerPawn.IsColonist) continue;
                if (corpse.StoringThing() != null) continue;

                unburied = corpse;
                break;
            }
            if (unburied == null) return;

            float severity;
            if (!complaints.TryGetValue(DefectKind.UnburiedDead, out severity)) severity = 0.5f;

            var defect = new ColonyDefect();
            defect.kind = DefectKind.UnburiedDead;
            defect.remedy = RemedyKind.BuryDead;
            defect.thing = unburied;
            defect.cell = unburied.Position;
            defect.severity = severity;
            defect.what = unburied.LabelCap + " is still lying at " + unburied.Position;
            defects.Add(defect);
        }

        // ------------------------------------------------------------ comforts

        /// <summary>
        /// Complaints the colony carries wherever it goes, so no particular room is at fault:
        /// nothing to eat off, nothing to do. The remedy picks somewhere to put the answer.
        /// </summary>
        static void SurveyComforts(Dictionary<DefectKind, float> complaints,
                                   List<ColonyDefect> defects)
        {
            AddIfFelt(complaints, defects, DefectKind.NoTable, "nowhere to eat off a table");
            AddIfFelt(complaints, defects, DefectKind.ColdRoom, "colonists are cold");

            // Heat, which every other part of this already knew how to answer.
            //
            // `EnvironmentHot` and `SleptInHeat` both map to HotRoom, HotRoom maps to AddCooler,
            // HotRoom carries the same 1.3 weight as cold because both kill, and AddCooler knows
            // to fall back on a passive cooler — fifty wood, no research, no grid. Every piece of
            // the answer was in place and wired to every other piece. Nothing ever asked whether
            // anybody was hot, so AddCooler could not fire from this path at all.
            //
            // A colony burned to death at 45C in one run and another put a colonist on the floor
            // at 51C, with the unmet-complaint list carrying no mention of heat on either
            // occasion. That is the tell: not a remedy that failed, but a number that never moved.
            AddIfFelt(complaints, defects, DefectKind.HotRoom, "colonists are too hot");

            AddIfFelt(complaints, defects, DefectKind.Cheerless, "nothing to do but work");
        }

        static void AddIfFelt(Dictionary<DefectKind, float> complaints, List<ColonyDefect> defects,
                              DefectKind kind, string what)
        {
            float severity;
            if (!complaints.TryGetValue(kind, out severity)) return;

            var defect = new ColonyDefect();
            defect.kind = kind;
            defect.remedy = DefectPolicy.RemedyFor(kind);
            defect.severity = severity;
            defect.what = what;
            defects.Add(defect);
        }

        // ------------------------------------------------------------ rooms

        static void SurveyRooms(Map map, ColonyState state, float means,
                                Dictionary<DefectKind, float> complaints, List<ColonyDefect> defects,
                                float essentialWeight, float occupancyWeight)
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

                int before = defects.Count;
                InspectBedroom(map, room, means, complaints, defects);

                // How much this particular room's faults matter. A three-person barracks and a
                // spare room nobody sleeps in produce the same defects and are not the same
                // problem — which the ranking could not previously express, because it saw the
                // fault and never the place.
                var facts = new Rooms.RoomFacts();
                facts.users = SleepersIn(map, room, state);
                facts.colonists = state.colonists;
                facts.essential = false;    // bedrooms are not what the colony runs on
                facts.unique = false;

                float importance = Rooms.RoomImportance.Of(facts, essentialWeight, occupancyWeight);
                for (int d = before; d < defects.Count; d++) defects[d].roomImportance = importance;
            }
        }

        /// <summary>Colonists who actually sleep in this room, by owned bed.</summary>
        static int SleepersIn(Map map, Room room, ColonyState state)
        {
            int sleepers = 0;
            for (int i = 0; i < state.allColonists.Count; i++)
            {
                var pawn = state.allColonists[i];
                if (pawn == null || pawn.ownership == null) continue;

                var bed = pawn.ownership.OwnedBed;
                if (bed == null || !bed.Spawned) continue;
                if (bed.GetRoom() == room) sleepers++;
            }
            return sleepers;
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
                                       List<ColonyDefect> defects, HashSet<RoomRole> rolesWanted)
        {
            if (layout == null || !BuildingMeans.Destitute(means)) return;

            var surplus = new List<PlannedRoom>();
            for (int i = 0; i < layout.rooms.Count; i++)
            {
                var planned = layout.rooms[i];

                // Only rooms the colony actually finished building.
                //
                // Reclaiming is for a colony that has fallen on hard times *since it built out* —
                // standing rooms it no longer needs, holding material it now does. A room still
                // going up is not that, and taking one produces a loop rather than material: the
                // planner wanted it a moment ago and wants it just as much afterwards, so it
                // sites the same room again, in the same place, and pays for the walls twice.
                //
                // Watched exactly that. A bedroom sited at (134,110) on day 1, its walls queued,
                // reclaimed at day 2 06h with means at 0.02, and sited again at (134,110) three
                // in-game hours later. Each lap spent material to recover less of it.
                //
                // A colony that cannot afford a room it has started is a real problem and it has
                // its own answer: consolidation withdraws the unstarted blueprints and leaves
                // every wall standing. That path loses nothing. This one was destroying work.
                if (!planned.furnitureQueued) continue;

                if (!Expendable(map, layout, planned, rolesWanted)) continue;
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
        /// <summary>
        /// Whether a room can be given up — taken down for its material, or handed a new job.
        ///
        /// Public because the planner needs the same answer for the opposite reason: before it
        /// opens ground for a new room it should know whether one the colony already owns is
        /// free to take the work.
        /// </summary>
        public static bool Expendable(Map map, BaseLayout layout, PlannedRoom planned)
        {
            return Expendable(map, layout, planned, null);
        }

        public static bool Expendable(Map map, BaseLayout layout, PlannedRoom planned,
                                      HashSet<RoomRole> rolesWanted)
        {
            // Never the last of a role any goal can ask for — satisfied or not.
            //
            // Run 96 built a Research room, finished it, and then reclaimed it for material
            // while the plan read "no research bench, so nothing the colony studies can ever
            // finish" — and immediately sited another one. That is the loop the furnitureQueued
            // rule above was written to stop, one step later: it protects a room still going up
            // and had nothing to say about a finished room the colony still needs.
            //
            // Asking the plan generalises the hardcoded floor rather than extending it. Kitchen,
            // Storage, Bedroom and Power are on that list because goals want them — Feed the
            // colony, Roofed storage, Shelter everyone, Power — so a rule that protects "the
            // last room any goal can ask for" covers all four and Research too, and covers
            // whatever is added next without anybody remembering to update a list.
            //
            // Satisfied goals count, and that correction cost a colony. The first version used
            // only *unsatisfied* goals, so a working Research room was unprotected precisely
            // because its bench was built — and pulling it down is what made the goal want one
            // again. A room is not spare because the goal it serves is met; it is met because
            // the room is standing.
            if (layout.CountRooms(planned.role) <= 1 &&
                rolesWanted != null && rolesWanted.Contains(planned.role))
                return false;

            // The original floor, kept for callers with no plan to consult.
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
                    // A lamp already on its way counts too, or the room is re-lit every pass.
                    var target = PlacementUtil.BuildTargetOf(thing);
                    if (target != null && target.HasComp(typeof(CompGlower))) return true;
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
