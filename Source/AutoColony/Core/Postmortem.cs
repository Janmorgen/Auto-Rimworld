using System.Globalization;
using System.Text;

namespace AutoColony
{
    /// <summary>Everything known about a colony at the moment it ended, with no game types attached.</summary>
    public struct LossEvidence
    {
        public int day;

        /// <summary>Observations the epoch got. Below a handful, none of the averages mean much.</summary>
        public int samples;

        /// <summary>Food in the larder at the end, and the lowest it reached during the epoch.</summary>
        public float daysOfFood;
        public float minDaysOfFood;

        public float avgMood;
        public float avgHealth;

        /// <summary>Fraction of the epoch's observations in which someone was down / on fire / breaking.</summary>
        public float downedFraction;
        public float fireFraction;
        public float mentalBreakFraction;

        public int deaths;
        public int raids;

        /// <summary>The complaint costing the most mood that upkeep had no remedy for.</summary>
        public string worstComplaint;
        public float worstComplaintMood;
    }

    /// <summary>
    /// Says what killed a colony, in one line, at the moment it dies.
    ///
    /// Every post-mortem of the overnight run was reconstructed by hand from the fifty lines
    /// before the failure, because the record said only "COLONY LOST" and a score. Everything
    /// needed was already in memory at that instant and was simply not written down.
    ///
    /// It names a primary cause and then lists what else was true, because colony deaths are
    /// chains and the end state routinely misattributes them — a frozen corpse says "cold" and
    /// says nothing about the raider an hour earlier. The contributing list is what lets a
    /// reader disagree with the verdict without re-reading the log.
    /// </summary>
    public static class Postmortem
    {
        /// <summary>Larder counts as empty below this many days.</summary>
        const float EmptyLarder = 0.25f;

        /// <summary>Food in store that makes "they could not reach it" the better explanation.</summary>
        const float FoodWasThere = 1f;

        /// <summary>Fraction of the epoch spent with someone down before that is the story.</summary>
        const float ProlongedDowned = 0.25f;

        const float ProlongedFire = 0.2f;
        const float ProlongedBreaking = 0.2f;
        const float MoodCollapse = 0.3f;

        /// <summary>
        /// The single most likely cause. Ordered by how directly the evidence implicates it,
        /// not by how common it is — starvation is checked first because an empty larder is
        /// unambiguous, where a raid is only ever circumstantial.
        /// </summary>
        public static string Cause(LossEvidence e)
        {
            // Every term below the counters is an average over the epoch's observations, and
            // with none taken they all read as zero — which looks exactly like a colony that
            // starved, burned and lost its mind at once. An unobserved epoch knows nothing.
            bool observed = e.samples > 0;
            bool larderRanDry = observed && e.minDaysOfFood <= EmptyLarder;

            if (larderRanDry && e.daysOfFood <= EmptyLarder) return "starvation";

            // Food in the store and everyone on the floor: the colony did not run out, it ran
            // out of people able to carry it. This is its own failure and wants its own name,
            // because the remedy is holding a colonist back rather than stocking more food.
            if (observed && e.downedFraction >= ProlongedDowned && e.minDaysOfFood >= FoodWasThere)
                return "incapacity";

            if (e.raids > 0 && e.deaths > 0) return "raid";
            if (observed && e.fireFraction >= ProlongedFire) return "fire";
            if (observed && (e.mentalBreakFraction >= ProlongedBreaking || e.avgMood < MoodCollapse))
                return "mood collapse";
            if (larderRanDry) return "starvation";
            if (e.deaths > 0) return "attrition";

            return "unexplained";
        }

        /// <summary>The one line written to the chronicle when the colony ends.</summary>
        public static string Describe(LossEvidence e)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            sb.Append("COLONY LOST on day ").Append(e.day).Append(" — ").Append(Cause(e));

            if (e.samples < EpochAccumulator.MinSamplesToScore)
            {
                sb.Append(": only ").Append(e.samples)
                  .Append(" observations, so nothing below is reliable");
            }

            sb.Append(": ").Append(e.daysOfFood.ToString("0.0", c)).Append("d food in store");
            if (e.samples > 0 && e.minDaysOfFood < e.daysOfFood)
                sb.Append(" (low ").Append(e.minDaysOfFood.ToString("0.0", c)).Append("d)");

            sb.Append(", mood ").Append(e.avgMood.ToString("0.00", c));
            sb.Append(", health ").Append(e.avgHealth.ToString("0.00", c));

            var also = Contributing(e);
            if (also.Length > 0) sb.Append(" — also: ").Append(also);

            return sb.ToString();
        }

        /// <summary>
        /// Everything else that was true, whether or not it was named the cause. This is the
        /// chain; the verdict above is only its last link.
        /// </summary>
        static string Contributing(LossEvidence e)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();

            if (e.downedFraction > 0f)
                Add(sb, "someone down for " + Percent(e.downedFraction) + " of the epoch");
            if (e.fireFraction > 0f)
                Add(sb, "fire burning for " + Percent(e.fireFraction));
            if (e.mentalBreakFraction > 0f)
                Add(sb, "breaking for " + Percent(e.mentalBreakFraction));
            if (e.raids > 0) Add(sb, e.raids + (e.raids == 1 ? " raid" : " raids"));
            if (e.deaths > 0) Add(sb, e.deaths + (e.deaths == 1 ? " death" : " deaths"));
            if (!string.IsNullOrEmpty(e.worstComplaint))
                Add(sb, "worst unmet complaint " + e.worstComplaint + " at " +
                        e.worstComplaintMood.ToString("0.0", c));

            return sb.ToString();
        }

        static void Add(StringBuilder sb, string part)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(part);
        }

        static string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
