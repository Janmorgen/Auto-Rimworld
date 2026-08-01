namespace AutoColony.Upkeep
{
    /// <summary>
    /// How comfortably the colony can afford to build, 0 (destitute) to 1 (comfortable).
    ///
    /// The same idea as combat desperation, applied to construction: how much room the colony
    /// has to be choosy. A colony with material to spare should give everyone their own bedroom,
    /// because sharing costs a great deal of mood. A colony with nothing should put every bed in
    /// one room and be glad of it — and one that has already built out and then fallen on hard
    /// times should be able to take the rooms back down, because the walls *are* the stockpile.
    /// Several hundred units of material stand in them, reachable only by deconstructing.
    ///
    /// So privacy is not a fixed goal. It is what the colony buys when it can afford to, and
    /// sells back when it cannot — and neither direction is the right answer on its own.
    ///
    /// Free of game types so the judgement can be tested offline.
    /// </summary>
    public static class BuildingMeans
    {
        /// <summary>
        /// Roughly what a room shell costs. A seven-cell room is about twenty-four wall segments
        /// at five material each; the figure only has to be the right order of magnitude, since
        /// it is dividing into a comparison rather than being spent.
        /// </summary>
        public const int RoomCost = 120;

        /// <summary>Below this the colony is treated as destitute and should consolidate.</summary>
        public const float DestituteBelow = 0.25f;

        /// <summary>At or above this it can afford a room per colonist.</summary>
        public const float ComfortableAbove = 0.75f;

        /// <summary>
        /// <paramref name="usableMaterial"/> is everything the colony could actually build with,
        /// loose stacks included — not just what sits in a stockpile.
        /// </summary>
        public static float Assess(int usableMaterial, int colonists)
        {
            if (colonists <= 0) return 1f;
            if (usableMaterial <= 0) return 0f;

            float roomsAffordable = usableMaterial / (float)RoomCost;
            return AcMath.Clamp01(roomsAffordable / colonists);
        }

        public static bool Destitute(float means) { return means < DestituteBelow; }
        public static bool Comfortable(float means) { return means >= ComfortableAbove; }

        /// <summary>
        /// How many rooms may stand unfinished at once.
        ///
        /// Material is not the only thing a room costs; it costs labour, and labour does not
        /// pool the way material does. Two colonists spread across six shells have each of them
        /// a third built and nowhere to sleep, where the same two on one shell have a bedroom by
        /// nightfall. The difference is not effort, it is only what the effort was pointed at.
        ///
        /// Watched happen: six shells queued in three days, none finished, no bed ever placed,
        /// and a colony that died with -12 mood a survey from sleeping on wet ground while 842
        /// units of material sat in the stockpile. Means said it could afford to build. It could;
        /// it could not afford to build six things.
        /// </summary>
        public static int ConcurrentRooms(int builders)
        {
            if (builders <= 2) return 1;
            return 1 + builders / 3;
        }

        /// <summary>
        /// How many beds to put in one room.
        ///
        /// A comfortable colony honours the strategy's own preference, which is usually one or
        /// two. A destitute one packs everybody into a single room, because a barracks nobody
        /// enjoys still beats sleeping outdoors and still beats spending the last of the wood on
        /// walls instead of a stove.
        /// </summary>
        public static int BedsPerRoom(float means, int preferred, int colonists)
        {
            if (preferred < 1) preferred = 1;
            if (colonists < 1) colonists = 1;

            if (Comfortable(means)) return preferred;
            if (Destitute(means)) return colonists;

            // Between the two, scale from everyone-in-one-room to the preference.
            float t = (means - DestituteBelow) / (ComfortableAbove - DestituteBelow);
            float beds = colonists + (preferred - colonists) * AcMath.Clamp01(t);

            int rounded = (int)(beds + 0.5f);
            if (rounded < preferred) rounded = preferred > colonists ? colonists : preferred;
            return rounded < 1 ? 1 : rounded;
        }

        /// <summary>
        /// How much a shared bedroom is worth complaining about.
        ///
        /// Nothing at all when the colony cannot afford to split it. Treating a barracks as a
        /// defect regardless would have the director tear beds out of the one warm room a
        /// struggling colony has, which is the opposite of help.
        /// </summary>
        public static float SharingSeverity(float means, float moodSeverity)
        {
            if (Destitute(means)) return 0f;
            return moodSeverity * AcMath.Clamp01((means - DestituteBelow) / (1f - DestituteBelow));
        }

        /// <summary>
        /// How badly the colony wants its materials back out of the walls.
        ///
        /// Only once it is genuinely short, and only worth it when there is something surplus to
        /// take down. Scales up as means fall, so a colony that is merely thrifty does not start
        /// demolishing itself.
        /// </summary>
        public static float ReclaimSeverity(float means, int surplusRooms)
        {
            if (surplusRooms <= 0) return 0f;
            if (means >= DestituteBelow) return 0f;

            float shortage = AcMath.Clamp01((DestituteBelow - means) / DestituteBelow);
            float scale = surplusRooms >= 2 ? 1f : 0.7f;
            return AcMath.Clamp01(shortage * scale);
        }

    }
}
