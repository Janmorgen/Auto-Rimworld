using System;
using System.Collections.Generic;
using AutoColony;
using RimWorld;
using Verse;

namespace AutoColony.Furniture
{
    /// <summary>
    /// Which furniture is useless without something to sit on.
    ///
    /// A chess table with no chair beside it is not a joy building; it is a decoration that cost
    /// material. The colony had one for eleven days in run 108 while the game's own
    /// <c>Chess table needs chairs</c> alert sat on screen and mood fell to 0.15. Nothing in the
    /// director could see it, and the reason is structural: the defect survey reads colonist
    /// *thoughts*, and there is no thought for "I could not play chess". A colonist simply takes
    /// no joy from it and walks away. Every fault this project has found by reading moods was
    /// findable because the game complains; this class of fault is silent by construction.
    ///
    /// The eating side is the same shape and was worse, because it had a remedy that could not
    /// work. <c>AteWithoutTable</c> raises <c>NoTable</c>, whose remedy places a table — and a
    /// pawn reaches a table only through <c>Toils_Ingest.TryFindChairOrSpot</c>, which searches
    /// for a *chair* within <c>ingestible.chairSearchRadius</c> and validates it on
    /// <c>def.building.isSittable</c>. No chair, no table, thought unchanged, remedy fires again.
    /// Run 107 placed eight tables.
    ///
    /// Everything here is asked of the game's own defs, so a modded games table or a modded
    /// stool classifies without anybody editing a list.
    /// </summary>
    public static class SeatingRule
    {
        static readonly Dictionary<ushort, bool> needsSeatCache = new Dictionary<ushort, bool>();

        /// <summary>
        /// Something a colonist can sit on. This is the game's own test — the one
        /// <c>TryFindChairOrSpot</c> validates chairs with — so a stool, a dining chair, an
        /// armchair and anything a mod marks sittable all qualify, and a bed does not.
        /// </summary>
        public static bool IsSeat(ThingDef def)
        {
            return def != null && def.building != null && def.building.isSittable;
        }

        /// <summary>
        /// Whether this thing is unusable without a seat beside it.
        ///
        /// Two sources, both the game's:
        ///
        /// * <c>surfaceType == Eat</c> — a dining table. The chair is how a pawn gets to it.
        /// * a <see cref="JoyGiverDef"/> that lists the def, has <c>requireChair</c>, and whose
        ///   worker sits adjacent to what it is playing on.
        ///
        /// The second condition needs both halves. <c>requireChair</c> defaults to <c>true</c>
        /// (read out of the <c>JoyGiverDef</c> constructor's IL — no def in Core sets it except
        /// Game-of-Ur, which sets it false), so on its own it also catches horseshoes, hoopstone
        /// and billiards, none of which are played sitting down. Pairing it with the worker class
        /// selects chess and poker and nothing else — which is exactly the set RimWorld ships an
        /// alert for, in <c>Alert_ChessTableNoChairs</c> and <c>Alert_PokerTableNoChairs</c>.
        /// Agreeing with the player's screen is the point.
        /// </summary>
        public static bool NeedsAdjacentSeat(ThingDef def)
        {
            if (def == null || def.building == null) return false;
            if (IsSeat(def)) return false;

            bool known;
            if (needsSeatCache.TryGetValue(def.shortHash, out known)) return known;

            bool needs = def.surfaceType == SurfaceType.Eat || PlayedSittingDown(def);
            needsSeatCache[def.shortHash] = needs;
            return needs;
        }

        static bool PlayedSittingDown(ThingDef def)
        {
            var givers = DefDatabase<JoyGiverDef>.AllDefsListForReading;
            for (int i = 0; i < givers.Count; i++)
            {
                var giver = givers[i];
                if (giver == null || !giver.requireChair) continue;
                if (giver.thingDefs == null || !giver.thingDefs.Contains(def)) continue;

                // The worker decides where the pawn stands. Only the sit-adjacent one seats them
                // against the building; watching and billiards happen on your feet.
                if (giver.giverClass == null) continue;
                if (typeof(JoyGiver_InteractBuildingSitAdjacent).IsAssignableFrom(giver.giverClass))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether a seat already stands beside this thing, or is on its way. A blueprint counts:
        /// treating one as absent is what queues the duplicate, which is the mistake that put
        /// eight tables in one colony.
        /// </summary>
        public static bool HasSeatAdjacent(Thing thing)
        {
            if (thing == null || thing.Map == null) return false;

            var map = thing.Map;
            foreach (var cell in Adjacent(thing))
            {
                if (!cell.InBounds(map)) continue;

                var things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (IsSeat(things[i].def)) return true;
                    if (IsSeat(PlacementUtil.BuildTargetOf(things[i]))) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The cells touching the thing's footprint — where a chair would have to go. Corners are
        /// excluded: a pawn sits square-on to what they are using.
        /// </summary>
        public static IEnumerable<IntVec3> Adjacent(Thing thing)
        {
            var rect = thing.OccupiedRect();
            return rect.ExpandedBy(1).EdgeCells;
        }

        /// <summary>
        /// The cheapest seat the colony is allowed to build. A stool is twenty-five of whatever is
        /// in store and needs no research; the dining chair is prettier and no more useful, and
        /// anything past those is behind Complex Furniture. Picked by cost from the defs rather
        /// than named, so a cheaper modded seat wins on its merits.
        /// </summary>
        public static ThingDef CheapestSeat()
        {
            ThingDef best = null;
            int bestCost = int.MaxValue;

            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (!IsSeat(def)) continue;
                if (!PlacementUtil.ResearchDone(def)) continue;

                // There was an `isEdifice` guard here, on the reasoning that a wall somebody can
                // perch on is not a chair. isEdifice is true of nearly all furniture — it is what
                // reserves the one large-thing slot in a cell — so the guard rejected every seat
                // in the game and CheapestSeat returned nothing. The seating scenario said
                // "cheapest buildable seat is NONE" beside thirteen correct classifications,
                // which is the only reason it was found before a colony ran on it. isSittable is
                // already the whole question; nothing needs to be subtracted from it.
                if (def.BaseMass <= 0f && def.costList == null && def.costStuffCount <= 0) continue;

                int cost = def.costStuffCount;
                if (def.costList != null)
                    for (int c = 0; c < def.costList.Count; c++) cost += def.costList[c].count;

                if (cost <= 0 || cost >= bestCost) continue;
                bestCost = cost;
                best = def;
            }
            return best;
        }
    }
}
