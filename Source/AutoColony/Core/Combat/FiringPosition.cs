namespace AutoColony.Combat
{
    /// <summary>What is true of a cell a colonist might fight from.</summary>
    public struct PositionFeatures
    {
        /// <summary>How much of an incoming shot the cell blocks, 0 (open ground) to 1.</summary>
        public float cover;

        /// <summary>Cells to the nearest hostile.</summary>
        public float toThreat;

        /// <summary>Cells to the nearest colonist already placed.</summary>
        public float toNearestAlly;

        /// <summary>Whether the cell is under a roof and inside walls.</summary>
        public bool indoors;

        /// <summary>Whether it stands where attackers must funnel — a doorway or a gap.</summary>
        public bool chokepoint;
    }

    /// <summary>How much each of those is worth to this colony. Every field is a gene.</summary>
    public struct PositionWeights
    {
        public float cover;
        public float standoff;
        public float preferredRange;
        public float spread;
        public float chokepoint;
        public float indoors;
    }

    /// <summary>
    /// Chooses where a colonist should stand to fight.
    ///
    /// The director had no concept of this at all. Every drafted colonist was sent to a single
    /// rally cell — the base origin — and told to shoot whatever was nearest. That is not a
    /// position, it is a coordinate: no cover, no spacing, no use made of a doorway, and a
    /// grenade or a mortar shell lands among all of them at once. A colony that fights well and
    /// one that fights badly were making the same move.
    ///
    /// This is deliberately a weighted score rather than a rule, and every weight is a gene.
    /// There is no single correct answer to how much cover is worth against how much spacing —
    /// it depends on whether the enemy has explosives, whether the colony has rifles or clubs,
    /// and how much of the base is walled. Those are the questions a search over many colonies
    /// can answer and a hardcoded pattern cannot even ask.
    ///
    /// Free of game types so the trade-off can be tested offline; gathering the features is the
    /// caller's job.
    /// </summary>
    public static class FiringPosition
    {
        /// <summary>
        /// How good this cell is to fight from, higher being better. Unbounded, since only the
        /// ordering matters.
        /// </summary>
        public static float Score(PositionFeatures f, PositionWeights w)
        {
            float score = 0f;

            // Cover is the one thing that reliably decides a firefight, so it is usually the
            // heaviest term — but a colony without ranged weapons wants to close, and the search
            // can express that by weighting it near zero.
            score += f.cover * w.cover;

            // Distance from the enemy, wanted around a preferred range rather than maximised:
            // too far and the colonist walks forward under fire anyway, too close and cover
            // stops mattering.
            float rangeError = f.toThreat - w.preferredRange;
            if (rangeError < 0f) rangeError = -rangeError;
            score -= rangeError * w.standoff;

            // Spacing. Everyone standing on one cell is what makes a single grenade decisive,
            // and it is exactly what a shared rally point produces.
            score += SpreadValue(f.toNearestAlly) * w.spread;

            if (f.chokepoint) score += w.chokepoint;
            if (f.indoors) score += w.indoors;

            return score;
        }

        /// <summary>
        /// Value of standing apart, rising quickly over the first few cells and flattening after.
        ///
        /// The difference between adjacent and three cells apart is most of the benefit; the
        /// difference between eight and eleven is nearly none, and chasing it would scatter the
        /// colony across the map for nothing.
        /// </summary>
        public static float SpreadValue(float toNearestAlly)
        {
            if (toNearestAlly <= 0f) return 0f;
            float capped = toNearestAlly > 5f ? 5f : toNearestAlly;
            return capped / 5f;
        }

        /// <summary>
        /// Whether one position is meaningfully better than another.
        ///
        /// Used to decide if a colonist should move at all: shuffling between near-identical
        /// cells wastes the seconds a firefight is decided in, and re-issuing an order restarts
        /// the job so they never actually shoot — the same trap the engage logic already hit.
        /// </summary>
        public const float WorthMovingFor = 0.75f;

        public static bool WorthMoving(float current, float candidate)
        {
            return candidate - current >= WorthMovingFor;
        }
    }
}
