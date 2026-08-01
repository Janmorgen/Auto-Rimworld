namespace AutoColony
{
    /// <summary>
    /// How big a raid the colony is inviting, and whether it can meet one.
    ///
    /// RimWorld sizes raids from the colony itself — principally its total wealth and how many
    /// colonists it has — so a colony that builds is quietly buying larger attacks. That makes
    /// defence a race rather than a threshold: what mattered last season is not what matters
    /// after a wing of stone bedrooms goes up, and nothing in the director could see the
    /// relationship at all. Fortification was ramped off raw wealth on a straight line, which
    /// says nothing about whether the colony is winning or losing the race.
    ///
    /// Free of game types so the arithmetic can be tested offline.
    /// </summary>
    public static class ThreatForecast
    {
        /// <summary>
        /// Wealth below which wealth contributes nothing to raid size, and the two documented
        /// points above it. The curve is piecewise linear between these.
        /// </summary>
        public const float WealthFloor = 14000f;
        public const float WealthMid = 400000f;
        public const float WealthCeiling = 1000000f;

        public const float PointsAtMid = 2400f;
        public const float PointsAtCeiling = 4200f;

        /// <summary>
        /// Raid points contributed by wealth alone, interpolated between the game's published
        /// anchors: nothing at 14k, 2400 at 400k, 4200 at a million, and flat above that.
        /// </summary>
        public static float PointsFromWealth(float wealth)
        {
            if (wealth <= WealthFloor) return 0f;
            if (wealth >= WealthCeiling) return PointsAtCeiling;

            if (wealth <= WealthMid)
                return PointsAtMid * (wealth - WealthFloor) / (WealthMid - WealthFloor);

            float t = (wealth - WealthMid) / (WealthCeiling - WealthMid);
            return PointsAtMid + (PointsAtCeiling - PointsAtMid) * t;
        }

        /// <summary>
        /// Points contributed per colonist.
        ///
        /// Approximate, and deliberately conservative. Colonist count is documented as one of the
        /// two largest player-controlled inputs alongside wealth, but not with the same published
        /// anchors — so this is a stated estimate rather than a reconstruction of the real
        /// formula, and it errs towards over-estimating the threat. Being surprised by a raid is
        /// far more expensive than one turret too many.
        /// </summary>
        public const float PointsPerColonist = 40f;

        public static float ExpectedRaidPoints(float wealth, int colonists)
        {
            if (colonists < 0) colonists = 0;
            return PointsFromWealth(wealth) + colonists * PointsPerColonist;
        }

        /// <summary>
        /// How well the colony's fighting strength covers the raid its own size is summoning,
        /// 0 (hopeless) to 1 (comfortable).
        ///
        /// Colony strength and raid points are different units, so this is a ratio against a
        /// reference rather than a physical comparison — what it is for is noticing the *trend*,
        /// which is the thing that was entirely invisible before. A colony whose readiness falls
        /// every season is losing a race it does not know it is in.
        /// </summary>
        public const float StrengthPerPoint = 0.25f;

        public static float Readiness(float colonyStrength, float expectedPoints)
        {
            if (expectedPoints <= 0f) return 1f;
            if (colonyStrength <= 0f) return 0f;
            return AcMath.Clamp01(colonyStrength / (expectedPoints * StrengthPerPoint));
        }

        /// <summary>Whether the colony is outgrowing what it can defend.</summary>
        public static bool Outgrowing(float colonyStrength, float wealth, int colonists)
        {
            return Readiness(colonyStrength, ExpectedRaidPoints(wealth, colonists)) < 0.5f;
        }
    }
}
