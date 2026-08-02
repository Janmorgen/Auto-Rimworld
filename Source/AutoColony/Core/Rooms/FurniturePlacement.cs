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

        /// <summary>Shelves and containers. What a workbench reaches into.</summary>
        Storage,

        /// <summary>Lamps and braziers. Wanted near what people do, not in a corner.</summary>
        Light,

        /// <summary>Everything else — decoration, oddments.</summary>
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

        /// <summary>
        /// Cells to the nearest piece of the kind this one works with.
        ///
        /// The same relation rooms have, one level down. A worktable reaches into a shelf for
        /// its ingredients and works measurably faster for having one close, and a lamp is worth
        /// having where people actually stand — neither of which the director could see, because
        /// it knew only that *something* was nearby and never what.
        /// </summary>
        public float fromPartnerFurniture;

        /// <summary>
        /// Share of the room's other furniture that belongs to the same purpose, 0 to 1.
        ///
        /// A workshop full of workshop things is a workshop; the same benches with a bed and a
        /// dining table among them is a cluttered bedroom that happens to contain a bench, and
        /// RimWorld's own room-role detection agrees.
        /// </summary>
        public float roomPurity;
    }

    /// <summary>How much each of those is worth for this kind of thing. Every field is a gene.</summary>
    public struct PlacementWeights
    {
        public float doorClearance;
        public float access;
        public float wallHugging;
        public float spacing;

        /// <summary>Value of standing near the kind of thing this one works with.</summary>
        public float partnerAffinity;

        /// <summary>Value of the room being given over to one purpose.</summary>
        public float purity;
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

            // Closeness to what this thing works with — a cost, unlike spacing, because here
            // near is better. A bench beside a shelf saves the walk on every ingredient.
            score -= Saturate(f.fromPartnerFurniture, 8f) * w.partnerAffinity;

            score += f.roomPurity * w.purity;

            return score;
        }

        /// <summary>
        /// What this kind of furniture wants to stand near.
        ///
        /// Facts about how the game works rather than preferences: a worktable draws from a
        /// shelf, and a lamp exists to light whatever people are doing. How much that closeness
        /// is worth is the gene.
        /// </summary>
        public static FurnitureKind? PartnerOf(FurnitureKind kind)
        {
            switch (kind)
            {
                case FurnitureKind.WorkTable: return FurnitureKind.Storage;
                case FurnitureKind.Storage: return FurnitureKind.WorkTable;
                case FurnitureKind.Light: return FurnitureKind.WorkTable;
                case FurnitureKind.Surface: return FurnitureKind.Light;
                default: return null;
            }
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
                    w.partnerAffinity = 0f;
                    w.purity = 1.5f;    // a bedroom with a workbench in it is not a bedroom
                    break;

                case FurnitureKind.WorkTable:
                    w.doorClearance = 0.8f;
                    w.access = 2.5f;
                    w.wallHugging = 0.5f;
                    w.spacing = 1.2f;
                    w.partnerAffinity = 2.0f;   // the shelf it reaches into
                    w.purity = 1.5f;
                    break;

                case FurnitureKind.Storage:
                    // Wants to be beside the bench that draws from it, and out of the way.
                    w.doorClearance = 0.6f;
                    w.access = 1.0f;
                    w.wallHugging = 1.8f;
                    w.spacing = 0.4f;
                    w.partnerAffinity = 2.2f;
                    w.purity = 0.8f;
                    break;

                case FurnitureKind.Surface:
                    w.doorClearance = 1.0f;
                    w.access = 2.0f;
                    w.wallHugging = 0f;
                    w.spacing = 1.5f;
                    w.partnerAffinity = 0.4f;
                    w.purity = 1.0f;
                    break;

                case FurnitureKind.Light:
                    // Near what people are doing, not tucked in a corner.
                    w.doorClearance = 0.3f;
                    w.access = 0.4f;
                    w.wallHugging = 1.0f;
                    w.spacing = 0.2f;
                    w.partnerAffinity = 1.6f;
                    w.purity = 0f;
                    break;

                default:
                    w.doorClearance = 0.5f;
                    w.access = 0.5f;
                    w.wallHugging = 0.8f;
                    w.spacing = 0.5f;
                    w.partnerAffinity = 0f;
                    w.purity = 0.3f;
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
        public const string Partner = "partner";
        public const string Purity = "purity";

        public static readonly string[] Aspects =
            { DoorClearance, Access, WallHugging, Spacing, Partner, Purity };
    }
}
