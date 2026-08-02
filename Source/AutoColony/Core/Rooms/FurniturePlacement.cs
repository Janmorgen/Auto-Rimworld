namespace AutoColony.Rooms
{
    /// <summary>What kind of thing is being put down, since they do not want the same spot.</summary>
    public enum FurnitureKind
    {
        /// <summary>Slept in. Wants a corner and quiet, not a thoroughfare.</summary>
        Bed,

        /// <summary>Worked at. Wants somewhere to stand and room to haul to.</summary>
        WorkTable,

        /// <summary>Eaten or sat at. Wants space around it on every side.</summary>
        Surface,

        /// <summary>Everything else — lamps, braziers, decoration.</summary>
        Other
    }

    /// <summary>What is true of a cell being considered for a piece of furniture.</summary>
    public struct PlacementFeatures
    {
        /// <summary>Cells to the door.</summary>
        public float fromDoor;

        /// <summary>How many of the four cardinal neighbours are open, 0 to 4.</summary>
        public int freeSides;

        /// <summary>Whether the cell backs onto a wall.</summary>
        public bool againstWall;

        /// <summary>Cells to the nearest furniture already placed or standing.</summary>
        public float fromOtherFurniture;
    }

    /// <summary>How much each of those is worth for this kind of thing. Every field is a gene.</summary>
    public struct PlacementWeights
    {
        public float doorClearance;
        public float access;
        public float wallHugging;
        public float spacing;
    }

    /// <summary>
    /// Where a piece of furniture should stand inside a room.
    ///
    /// Placement was first-fit: iterate the interior, drop the thing in the first legal cell,
    /// skip only the square in front of the door. Everything therefore piled into one corner of
    /// the room in iteration order, blocking its own access, leaving the rest of the floor empty
    /// and dragging the room's Space rating down for no benefit — and RimWorld scores a room on
    /// exactly that, along with beauty and cleanliness.
    ///
    /// What a good cell is depends on what is going in it, which is why the weights are per kind
    /// rather than global. A bed wants a corner, a wall at its back and distance from the door;
    /// a workbench wants open sides so somebody can stand at it and haul to it; a table wants
    /// space on every side because people sit round it. Those pull in opposite directions and
    /// there is no single ordering that serves all of them.
    ///
    /// Free of game types so the trade-offs can be tested offline.
    /// </summary>
    public static class FurniturePlacement
    {
        /// <summary>
        /// How good this cell is for this thing, higher being better.
        ///
        /// Door clearance is a *reward for distance* rather than a hard exclusion, because the
        /// right distance differs: a bed across the room from the door is ideal, a lamp beside
        /// it is fine, and a rule that only banned the one square in front of the door expressed
        /// neither.
        /// </summary>
        public static float Score(PlacementFeatures f, PlacementWeights w)
        {
            float score = 0f;

            score += Saturate(f.fromDoor, 6f) * w.doorClearance;
            score += (f.freeSides / 4f) * w.access;
            if (f.againstWall) score += w.wallHugging;
            score += Saturate(f.fromOtherFurniture, 4f) * w.spacing;

            return score;
        }

        /// <summary>
        /// Rises with distance and flattens, so nothing is pushed into a far corner chasing a
        /// benefit that stopped accruing several cells ago.
        /// </summary>
        public static float Saturate(float distance, float full)
        {
            if (distance <= 0f) return 0f;
            if (full <= 0f) return 1f;
            return distance >= full ? 1f : distance / full;
        }

        /// <summary>
        /// Sensible opening values per kind — the starting point the search moves away from,
        /// not the answer. Bed backs into a corner away from the door; workbench prizes access
        /// above all; a table wants elbow room and does not care about walls.
        /// </summary>
        public static PlacementWeights DefaultsFor(FurnitureKind kind)
        {
            var w = new PlacementWeights();
            switch (kind)
            {
                case FurnitureKind.Bed:
                    w.doorClearance = 2.0f;
                    w.access = 0.3f;
                    w.wallHugging = 1.5f;
                    w.spacing = 1.0f;
                    break;

                case FurnitureKind.WorkTable:
                    w.doorClearance = 0.8f;
                    w.access = 2.5f;
                    w.wallHugging = 0.5f;
                    w.spacing = 1.2f;
                    break;

                case FurnitureKind.Surface:
                    w.doorClearance = 1.0f;
                    w.access = 2.0f;
                    w.wallHugging = 0f;
                    w.spacing = 1.5f;
                    break;

                default:
                    w.doorClearance = 0.5f;
                    w.access = 0.5f;
                    w.wallHugging = 0.8f;
                    w.spacing = 0.5f;
                    break;
            }
            return w;
        }

        /// <summary>The gene name for one aspect of one kind.</summary>
        public static string GeneKey(FurnitureKind kind, string aspect)
        {
            return "furniture." + kind + "." + aspect;
        }

        public const string DoorClearance = "doorClearance";
        public const string Access = "access";
        public const string WallHugging = "wallHugging";
        public const string Spacing = "spacing";

        public static readonly string[] Aspects = { DoorClearance, Access, WallHugging, Spacing };
    }
}
