namespace AutoColony.Rooms
{
    /// <summary>
    /// The grid of places a room may go.
    ///
    /// Sites used to be a line. Slots alternated north and south of a two-cell corridor and
    /// fanned left and right from the origin, so every room in every colony landed in one of
    /// exactly two rows — and the per-role scoring built on top of it could only ever choose
    /// *where along the line*. A workshop that wanted to be beside the store got no closer than
    /// however many slots separated them, a freezer could not sit behind the kitchen, and a base
    /// of a dozen rooms was a hundred cells wide and two rooms tall: the worst shape there is
    /// both for walking across and for defending.
    ///
    /// This is the same pitch offered in two dimensions instead of one. Neighbours still share
    /// a wall, because a shared wall is a wall nobody builds twice and that was always the good
    /// part of the corridor.
    ///
    /// Rows are numbered outward from the corridor in both directions — row 0 is the first row
    /// north of it, row -1 the first row south — so the corridor itself is the one strip of
    /// ground no room can ever be sited on. That is deliberate, and it is what keeps a base
    /// that fills in from every side from sealing itself off.
    ///
    /// Free of game types so the geometry can be argued with in a test.
    /// </summary>
    public static class RoomLattice
    {
        /// <summary>Cells of corridor kept clear north of the origin row.</summary>
        public const int CorridorHeight = 2;

        /// <summary>
        /// Which ring a position sits in, counting outward from the corridor.
        ///
        /// Rows -1 and 0 both sit against the corridor and must rank equally, or a search
        /// working outward would offer every site in the north half of the base before the first
        /// one to the south of it — which is the old line's bias reintroduced by the iteration
        /// order rather than by the geometry.
        /// </summary>
        public static int Ring(int col, int row)
        {
            int vertical = row >= 0 ? row : -row - 1;
            int lateral = col >= 0 ? col : -col;
            return lateral > vertical ? lateral : vertical;
        }

        /// <summary>
        /// The lowest row index in a ring. Rings are one row taller than they are wide, because
        /// the corridor splits the middle row in two.
        /// </summary>
        public static int LowestRow(int ring) { return -(ring + 1); }

        /// <summary>The highest row index in a ring.</summary>
        public static int HighestRow(int ring) { return ring; }

        /// <summary>
        /// West edge of the footprint at a position. The pitch is width-1 so the east wall of
        /// one room is the west wall of the next.
        /// </summary>
        public static int MinX(int originX, int col, int width)
        {
            return originX + col * (width - 1);
        }

        /// <summary>
        /// South edge of the footprint at a position, measured away from the corridor in
        /// whichever direction the row lies.
        /// </summary>
        public static int MinZ(int originZ, int row, int height)
        {
            return row >= 0
                ? originZ + CorridorHeight + row * (height - 1)
                : originZ - 1 + row * (height - 1);
        }
    }
}
