namespace AutoColony
{
    /// <summary>
    /// How far ahead the colony has to look when it decides to feed itself.
    ///
    /// Deciding to hunt is not eating. The animal has to be killed, the corpse hauled, the meat
    /// butchered and a meal cooked, and each of those is a separate job queued behind everything
    /// else the colony is doing. Measuring urgency against the food actually in the larder
    /// therefore escalates far too late: at one day of food there is no margin left for the hunt
    /// to fail, for the hunter to be shot, or for nobody to be free to cook. Most of the colonies
    /// lost over a ten-hour unattended run died inside that window, with an approved hunt
    /// underway and nothing to eat before it landed.
    ///
    /// So urgency is measured against the food that will be left by the time anything decided
    /// now could arrive. That moves every food decision earlier by the same amount without
    /// altering any of the judgements built on top of it.
    /// </summary>
    public static class FoodTiming
    {
        /// <summary>
        /// Days between designating food and eating it: kill, haul, butcher, cook.
        ///
        /// A constant rather than a gene, deliberately. It is a fact about how long RimWorld's
        /// jobs take rather than a strategy worth searching over, and the search is already
        /// starved of samples against the genes it has.
        /// </summary>
        public const float SupplyLeadDays = 1.5f;

        /// <summary>Food still in hand by the time a decision taken now could put a meal on the table.</summary>
        public static float EffectiveDays(float daysOfFood)
        {
            float left = daysOfFood - SupplyLeadDays;
            return left > 0f ? left : 0f;
        }

        /// <summary>
        /// 0 when comfortably stocked, 1 when the larder will be empty before anything the
        /// colony decides now can reach it.
        /// </summary>
        public static float Urgency(float daysOfFood, float targetDays)
        {
            if (targetDays <= 0f) return 1f;
            return AcMath.Clamp01(1f - EffectiveDays(daysOfFood) / targetDays);
        }
    }
}
