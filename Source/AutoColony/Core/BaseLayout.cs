using System.Collections.Generic;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// The kinds of room the planner knows how to build.
    ///
    /// Nine of these have a counterpart in RimWorld's own fifteen <c>RoomRoleDef</c>s, which is
    /// what decides whether the game agrees the room is what the layout calls it. Power and
    /// Freezer deliberately do not: the game has no classification for machinery, and neither
    /// wants one.
    ///
    /// Added at the end on purpose. The role is saved by name rather than by number, so the
    /// existing values could be reordered safely — but the siting genes are registered by
    /// walking this enum, so every addition grows the genome and makes older archives a
    /// different shape.
    /// </summary>
    public enum RoomRole
    {
        Storage = 0,
        Kitchen = 1,
        Dining = 2,
        Bedroom = 3,
        Workshop = 4,
        Research = 5,
        Hospital = 6,
        Prison = 7,
        Power = 8,
        Freezer = 9,

        /// <summary>
        /// Somewhere to do something that is not work.
        ///
        /// The joy buildings existed already and had nowhere to live: they were placed by an
        /// upkeep *remedy* into the first planned room with a free cell, so they scattered
        /// through kitchens and bedrooms and no room ever gathered enough of them to read as a
        /// rec room. RimWorld pays up to +8 mood for recreation taken in an impressive one —
        /// every stage of that thought is positive, there is no downside band — and none of it
        /// was reachable.
        /// </summary>
        Recreation = 10,

        /// <summary>
        /// Somewhere to put the dead.
        ///
        /// Graves were dropped on open ground by a radial search around wherever the body fell.
        /// A tomb keeps them together and out of the weather, and multiplies what colonists get
        /// from visiting by up to 1.4.
        /// </summary>
        Tomb = 11,

        /// <summary>
        /// Somewhere for tamed animals to sleep and eat.
        ///
        /// The one role here with no existing subsystem behind it — nothing tames, trains or
        /// breeds anything, so this builds the shelter and the feeding, and animals acquired
        /// any other way have somewhere to be.
        /// </summary>
        Barn = 12
    }

    /// <summary>A room the planner has reserved, and how far construction has got.</summary>
    public class PlannedRoom : IExposable
    {
        public int minX;
        public int minZ;
        public int width = 7;
        public int height = 7;
        public RoomRole role;
        public bool wallsQueued;

        /// <summary>
        /// A stable name for this room's place on the map, for anything keeping a record about
        /// it across passes.
        ///
        /// Lives here rather than in either module that uses it, because the whole point of
        /// <see cref="Churn"/> is that two modules recognise they are arguing about the same
        /// room, and two modules deriving "the same room" separately is the exact mistake that
        /// produced the argument. One derivation or none.
        /// </summary>
        public int PlaceKey { get { return minX * 4096 + minZ; } }

        /// <summary>
        /// The Space band the room scored when its walls closed, before any furniture went in.
        ///
        /// Space is decided by the walls and nothing else, but the *measurement* is not: cells
        /// under an impassable building leave the room's region, so a furnished room reads
        /// smaller than the shell that was built. A 7x7 kitchen rated average-sized empty and
        /// rather tight once it held a stove and a butcher table — and judging it live would
        /// mark it down for owning the equipment it exists for.
        ///
        /// -1 until the room has been finished and rated once.
        /// </summary>
        public int shellSpaceStage = -1;
        public bool furnitureQueued;
        public int doorX;
        public int doorZ;

        /// <summary>
        /// When this room last changed what it is for, so it cannot be changed again straight
        /// away. -1 for a room that has never been repurposed.
        /// </summary>
        public int roleChangedTick = -1;

        /// <summary>
        /// How many times the colony has started this room's walls over. A site it cannot
        /// finish has to be given up eventually, and this is the only evidence that it is one.
        /// </summary>
        public int shellAttempts;

        /// <summary>
        /// Tick until which this room is set aside, because the colony has more open than it can
        /// finish. It keeps its site and its plan; it simply is not worked on meanwhile.
        /// </summary>
        public int deferredUntilTick = -1;

        public CellRect Rect { get { return new CellRect(minX, minZ, width, height); } }

        /// <summary>The floor area inside the walls.</summary>
        public CellRect Interior
        {
            get { return new CellRect(minX + 1, minZ + 1, width - 2, height - 2); }
        }

        public IntVec3 Door { get { return new IntVec3(doorX, 0, doorZ); } }

        public IntVec3 Center
        {
            get { return new IntVec3(minX + width / 2, 0, minZ + height / 2); }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref minX, "minX", 0);
            Scribe_Values.Look(ref minZ, "minZ", 0);
            Scribe_Values.Look(ref width, "width", 7);
            Scribe_Values.Look(ref height, "height", 7);
            Scribe_Values.Look(ref role, "role", RoomRole.Storage);
            Scribe_Values.Look(ref shellSpaceStage, "shellSpaceStage", -1);
            Scribe_Values.Look(ref wallsQueued, "wallsQueued", false);
            Scribe_Values.Look(ref furnitureQueued, "furnitureQueued", false);
            Scribe_Values.Look(ref doorX, "doorX", 0);
            Scribe_Values.Look(ref doorZ, "doorZ", 0);
            Scribe_Values.Look(ref roleChangedTick, "roleChangedTick", -1);
            Scribe_Values.Look(ref shellAttempts, "shellAttempts", 0);
            Scribe_Values.Look(ref deferredUntilTick, "deferredUntilTick", -1);
        }
    }

    /// <summary>
    /// The colony's master plan: where the base sits and which rooms have been reserved.
    ///
    /// Persisted with the save so construction resumes exactly where it left off rather than
    /// re-deriving a different layout every time the game is loaded.
    /// </summary>
    /// <summary>
    /// Ground the colony has committed to something that is not a room.
    ///
    /// A pen is the case this exists for. It is not a room and never will be — no walls, no
    /// roof, no role — so none of the room machinery applies to it, and consequently nothing
    /// stopped a room being sited straight across one. That does not read as a collision when
    /// it happens: ClearFootprint mines natural rock only and leaves colony buildings alone, so
    /// the fence stays standing and the wall blueprints simply cannot be placed on those cells.
    /// The room then never closes, and the planner counts it as outstanding work for the rest
    /// of the colony's life, holding a concurrency slot that never comes back.
    /// </summary>
    public class ReservedGround : IExposable
    {
        public CellRect rect;
        public string what;

        public ReservedGround() { }

        public ReservedGround(CellRect rect, string what)
        {
            this.rect = rect;
            this.what = what;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref rect, "rect");
            Scribe_Values.Look(ref what, "what");
        }
    }

    public class BaseLayout : IExposable
    {
        public bool established;
        public IntVec3 origin = IntVec3.Invalid;
        public List<PlannedRoom> rooms = new List<PlannedRoom>();

        /// <summary>Ground claimed by something that is not a room — see <see cref="ReservedGround"/>.</summary>
        public List<ReservedGround> reserved = new List<ReservedGround>();

        /// <summary>Whether a rectangle runs across ground already claimed for something else.</summary>
        public bool IsReserved(CellRect rect)
        {
            if (reserved == null) return false;
            for (int i = 0; i < reserved.Count; i++)
                if (reserved[i] != null && rect.Overlaps(reserved[i].rect)) return true;
            return false;
        }

        /// <summary>Whether a rectangle runs across the interior of a room already planned.</summary>
        public bool OverlapsAnyRoom(CellRect rect)
        {
            for (int i = 0; i < rooms.Count; i++)
                if (rect.Overlaps(rooms[i].Rect)) return true;
            return false;
        }

        /// <summary>Next unused slot index along the corridor, used when reserving a new room.</summary>
        public int nextSlot;

        public bool HasRoom(RoomRole role)
        {
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].role == role) return true;
            return false;
        }

        public int CountRooms(RoomRole role)
        {
            int n = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].role == role) n++;
            return n;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref established, "established", false);
            Scribe_Values.Look(ref origin, "origin", IntVec3.Invalid);
            Scribe_Values.Look(ref nextSlot, "nextSlot", 0);
            Scribe_Collections.Look(ref rooms, "rooms", LookMode.Deep);
            Scribe_Collections.Look(ref reserved, "reserved", LookMode.Deep);
            if (reserved == null) reserved = new List<ReservedGround>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rooms == null)
                rooms = new List<PlannedRoom>();
        }
    }
}
