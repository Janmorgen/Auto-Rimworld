using System.Collections.Generic;
using Verse;

namespace AutoColony
{
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
        Freezer = 9
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
        public bool furnitureQueued;
        public int doorX;
        public int doorZ;

        /// <summary>
        /// When this room last changed what it is for, so it cannot be changed again straight
        /// away. -1 for a room that has never been repurposed.
        /// </summary>
        public int roleChangedTick = -1;

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
            Scribe_Values.Look(ref wallsQueued, "wallsQueued", false);
            Scribe_Values.Look(ref furnitureQueued, "furnitureQueued", false);
            Scribe_Values.Look(ref doorX, "doorX", 0);
            Scribe_Values.Look(ref doorZ, "doorZ", 0);
            Scribe_Values.Look(ref roleChangedTick, "roleChangedTick", -1);
        }
    }

    /// <summary>
    /// The colony's master plan: where the base sits and which rooms have been reserved.
    ///
    /// Persisted with the save so construction resumes exactly where it left off rather than
    /// re-deriving a different layout every time the game is loaded.
    /// </summary>
    public class BaseLayout : IExposable
    {
        public bool established;
        public IntVec3 origin = IntVec3.Invalid;
        public List<PlannedRoom> rooms = new List<PlannedRoom>();

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
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rooms == null)
                rooms = new List<PlannedRoom>();
        }
    }
}
