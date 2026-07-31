using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>One option's running statistics.</summary>
    public class BanditArm : IExposable
    {
        public string key = "";
        public float pulls;       // discounted count, so it decays as the colony changes
        public float rewardSum;   // discounted reward total
        public int rawPulls;      // undiscounted, for display

        public float Mean { get { return pulls > 0.0001f ? rewardSum / pulls : 0f; } }

        public void ExposeData()
        {
            Scribe_Values.Look(ref key, "key", "");
            Scribe_Values.Look(ref pulls, "pulls", 0f);
            Scribe_Values.Look(ref rewardSum, "rewardSum", 0f);
            Scribe_Values.Look(ref rawPulls, "rawPulls", 0);
        }
    }

    /// <summary>
    /// Discounted UCB1 over a set of named options.
    ///
    /// Discounting matters here: RimWorld is non-stationary — a research project or build
    /// that scored well in year one may be worthless in year five — so old observations
    /// decay rather than accumulating forever.
    /// </summary>
    public class Bandit : IExposable
    {
        /// <summary>Per-update decay applied to every arm's statistics.</summary>
        public const float Discount = 0.97f;

        Dictionary<string, BanditArm> arms = new Dictionary<string, BanditArm>();
        float totalPulls;

        // Own RNG rather than Verse.Rand: see AcRandom for why the learning layer must not
        // draw from the game's global stream.
        AcRandom rng = new AcRandom(0x5EEDB00CUL);

        public IEnumerable<BanditArm> Arms { get { return arms.Values; } }

        public BanditArm ArmFor(string key)
        {
            BanditArm a;
            if (!arms.TryGetValue(key, out a))
            {
                a = new BanditArm();
                a.key = key;
                arms[key] = a;
            }
            return a;
        }

        /// <summary>
        /// UCB score for one option. Untried options return a large finite value so they
        /// are tried first, without letting float.PositiveInfinity poison comparisons.
        /// </summary>
        public float Score(string key, float explore)
        {
            var arm = ArmFor(key);
            if (arm.pulls < 0.0001f) return 1000f;
            double bonus = explore * Math.Sqrt(2.0 * Math.Log(Math.Max(totalPulls, 1.0001f)) / arm.pulls);
            return arm.Mean + (float)bonus;
        }

        /// <summary>Picks the highest-scoring option, with random tie-breaking.</summary>
        public string Select(IList<string> candidates, float explore)
        {
            if (candidates == null || candidates.Count == 0) return null;
            string best = null;
            float bestScore = float.NegativeInfinity;
            int ties = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                float s = Score(candidates[i], explore);
                if (s > bestScore + 0.0001f)
                {
                    bestScore = s;
                    best = candidates[i];
                    ties = 1;
                }
                else if (s > bestScore - 0.0001f)
                {
                    // Reservoir tie-break so repeated equal scores don't always pick the first.
                    ties++;
                    if (rng.Range(0, ties) == 0) best = candidates[i];
                }
            }
            return best;
        }

        /// <summary>Records an outcome. <paramref name="reward"/> is expected in roughly [0,1].</summary>
        public void Update(string key, float reward)
        {
            if (string.IsNullOrEmpty(key)) return;

            foreach (var a in arms.Values)
            {
                a.pulls *= Discount;
                a.rewardSum *= Discount;
            }
            totalPulls *= Discount;

            var arm = ArmFor(key);
            arm.pulls += 1f;
            arm.rewardSum += reward;
            arm.rawPulls++;
            totalPulls += 1f;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref arms, "arms", LookMode.Value, LookMode.Deep);
            Scribe_Values.Look(ref totalPulls, "totalPulls", 0f);
            Scribe_Deep.Look(ref rng, "rng");
            if (Scribe.mode == LoadSaveMode.LoadingVars && arms == null)
                arms = new Dictionary<string, BanditArm>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rng == null)
                rng = new AcRandom(0x5EEDB00CUL);
        }

        public XElement ToXml(string name)
        {
            var el = new XElement(name, new XAttribute("total", totalPulls.ToString("R", CultureInfo.InvariantCulture)));
            foreach (var a in arms.Values)
            {
                el.Add(new XElement("arm",
                    new XAttribute("k", a.key),
                    new XAttribute("n", a.pulls.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("r", a.rewardSum.ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("raw", a.rawPulls)));
            }
            return el;
        }

        public static Bandit FromXml(XElement el)
        {
            var b = new Bandit();
            if (el == null) return b;
            var t = el.Attribute("total");
            float tv;
            if (t != null && float.TryParse(t.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tv))
                b.totalPulls = tv;

            foreach (var ae in el.Elements("arm"))
            {
                var k = ae.Attribute("k");
                if (k == null) continue;
                var arm = b.ArmFor(k.Value);
                float f;
                var n = ae.Attribute("n");
                if (n != null && float.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) arm.pulls = f;
                var r = ae.Attribute("r");
                if (r != null && float.TryParse(r.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) arm.rewardSum = f;
                int raw;
                var rw = ae.Attribute("raw");
                if (rw != null && int.TryParse(rw.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw)) arm.rawPulls = raw;
            }
            return b;
        }

        /// <summary>Merges another bandit's statistics into this one (used when seeding from the archive).</summary>
        public void MergeFrom(Bandit other, float weight)
        {
            if (other == null) return;
            foreach (var a in other.arms.Values)
            {
                var mine = ArmFor(a.key);
                mine.pulls += a.pulls * weight;
                mine.rewardSum += a.rewardSum * weight;
                mine.rawPulls += a.rawPulls;
            }
            totalPulls += other.totalPulls * weight;
        }
    }
}
