using System.Collections.Generic;

namespace AutoColony
{
    /// <summary>
    /// Things the colony wants and cannot have, and how long that has been true.
    ///
    /// Run 170 said this on day 0, hour 6, unprompted and entirely correctly:
    ///
    ///     "no herbal medicine: healroot needs Plants 8 and the best grower here has 4, so
    ///      wounds will be treated with whatever can be bought or found"
    ///
    /// That is a complete diagnosis — the capability, the skill that gates it, the threshold, and
    /// the shortfall. On day 13 the colony had zero medicine, one colonist bleeding out, and no
    /// trader had visited in thirteen days. Its entire answer to a capability it lacked was a
    /// channel it does not control, and nothing anywhere noticed the answer had not worked.
    ///
    /// The comment on the line that printed it already says what was missing: "this is a thing
    /// the colony wants and cannot have, and **that list is the roadmap**". The list was named as
    /// the valuable thing and never made. One message was printed once, a bool stopped it
    /// repeating, and the fact left no trace anything could reason about.
    ///
    /// So this is the list. It holds no policy and decides nothing — deliberately, because the
    /// last three instruments added before a decision each changed what the decision should be,
    /// and one of them refuted the change it was built to justify. What it makes possible:
    ///
    ///   - work assigned toward whoever is nearest a threshold, since skills rise with use and
    ///     Plants 4 becomes Plants 8 if somebody does the growing (#46 from the other side)
    ///   - a want that grows as its gap ages, rather than weighing the same on day 1 and day 13
    ///   - the fallback itself becoming measurable: "bought or found" produced nothing across
    ///     thirteen days and no number anywhere said so
    ///
    /// Free of game types so it can be argued with in a test.
    /// </summary>
    public static class CapabilityGaps
    {
        public class Gap
        {
            /// <summary>What the colony cannot do — "herbal medicine".</summary>
            public string capability;

            /// <summary>What gates it — a skill name, a research project, a building.</summary>
            public string gatedBy;

            /// <summary>The level or amount required.</summary>
            public float needed;

            /// <summary>The best the colony currently has.</summary>
            public float best;

            /// <summary>Tick it was first reported open.</summary>
            public int openedAt;

            /// <summary>Tick it was last confirmed still open.</summary>
            public int lastSeen;

            /// <summary>How far short, in the units of whatever gates it.</summary>
            public float Shortfall { get { return needed > best ? needed - best : 0f; } }
        }

        static readonly Dictionary<string, Gap> open = new Dictionary<string, Gap>();

        /// <summary>Forget everything. For tests and for a fresh colony.</summary>
        public static void Clear() { open.Clear(); }

        /// <summary>
        /// Note that the colony still cannot do this.
        ///
        /// Idempotent on purpose: a module reporting the same gap every pass is the normal case,
        /// and what matters is that the clock keeps running rather than restarting. The old
        /// behaviour — a bool that suppressed the message after the first time — is exactly how
        /// a gap that had stood for thirteen days looked identical to one found this minute.
        /// </summary>
        public static void Report(string capability, string gatedBy, float needed, float best,
                                  int nowTick)
        {
            if (string.IsNullOrEmpty(capability)) return;

            Gap g;
            if (!open.TryGetValue(capability, out g))
            {
                g = new Gap { capability = capability, openedAt = nowTick };
                open[capability] = g;
            }

            g.gatedBy = gatedBy;
            g.needed = needed;
            g.best = best;
            g.lastSeen = nowTick;
        }

        /// <summary>The colony can do this now. Closing is as much a fact as opening.</summary>
        public static void Close(string capability)
        {
            if (!string.IsNullOrEmpty(capability)) open.Remove(capability);
        }

        /// <summary>Whether this one is currently out of reach.</summary>
        public static bool IsOpen(string capability)
        {
            return !string.IsNullOrEmpty(capability) && open.ContainsKey(capability);
        }

        /// <summary>
        /// How long this gap has stood, in ticks, or -1 if it is not open.
        ///
        /// The number that did not exist. A want that has gone unmet for thirteen days is a
        /// different thing from one found this morning, and nothing could tell them apart.
        /// </summary>
        public static int StandingFor(string capability, int nowTick)
        {
            Gap g;
            if (string.IsNullOrEmpty(capability) || !open.TryGetValue(capability, out g))
                return -1;

            int age = nowTick - g.openedAt;
            return age < 0 ? 0 : age;
        }

        /// <summary>Everything currently out of reach, oldest first — the roadmap.</summary>
        public static List<Gap> All()
        {
            var all = new List<Gap>(open.Values);
            all.Sort((a, b) => a.openedAt.CompareTo(b.openedAt));
            return all;
        }

        /// <summary>The gap that has stood longest, or null if the colony wants nothing it lacks.</summary>
        public static Gap Oldest()
        {
            Gap oldest = null;
            foreach (var g in open.Values)
                if (oldest == null || g.openedAt < oldest.openedAt) oldest = g;
            return oldest;
        }

        /// <summary>One line per gap, for the record.</summary>
        public static string Explain(int nowTick)
        {
            var all = All();
            if (all.Count == 0) return "nothing the colony wants is out of reach";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < all.Count; i++)
            {
                if (sb.Length > 0) sb.Append("; ");
                var g = all[i];
                sb.Append(g.capability).Append(" needs ").Append(g.gatedBy).Append(' ')
                  .Append(g.needed.ToString("0.#")).Append(" and the best is ")
                  .Append(g.best.ToString("0.#"))
                  .Append(" — standing ")
                  .Append(((nowTick - g.openedAt) / 60000f).ToString("0.0")).Append(" days");
            }
            return sb.ToString();
        }
    }
}
