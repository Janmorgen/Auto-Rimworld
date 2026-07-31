using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>
    /// One candidate colony-management strategy: a value for every gene in <see cref="Genes"/>.
    /// Genes that were never explicitly set fall back to the spec default, so a genome saved
    /// by an older version stays loadable when new genes are introduced.
    /// </summary>
    public class StrategyGenome : IExposable
    {
        Dictionary<string, float> values = new Dictionary<string, float>();

        /// <summary>Human-readable provenance, e.g. "mutant of gen 12". Purely for the UI.</summary>
        public string lineage = "default";

        /// <summary>Generation counter, incremented each time a mutant is produced.</summary>
        public int generation;

        public StrategyGenome() { }

        public static StrategyGenome Default()
        {
            var g = new StrategyGenome();
            g.lineage = "defaults";
            return g;
        }

        public float Get(string key)
        {
            float v;
            if (values.TryGetValue(key, out v)) return v;
            var spec = Genes.Spec(key);
            return spec != null ? spec.Default : 0f;
        }

        /// <summary>Gene value as an int, rounded — for genes that index discrete choices.</summary>
        public int GetInt(string key)
        {
            return Mathf_RoundToInt(Get(key));
        }

        public void Set(string key, float value)
        {
            var spec = Genes.Spec(key);
            values[key] = spec != null ? spec.Clamp(value) : value;
        }

        public StrategyGenome Clone()
        {
            var g = new StrategyGenome();
            foreach (var kv in values) g.values[kv.Key] = kv.Value;
            g.lineage = lineage;
            g.generation = generation;
            return g;
        }

        /// <summary>
        /// Produces a mutated copy. Only a fraction of genes are perturbed per mutation
        /// (<paramref name="mutationRate"/>) so that a scored epoch reflects a small,
        /// attributable change rather than an entirely different strategy.
        /// </summary>
        public StrategyGenome Mutate(AcRandom rng, float sigma, float mutationRate)
        {
            var child = Clone();
            var specs = Genes.All;
            if (specs.Count == 0 || rng == null) return child;

            bool mutatedAny = false;
            for (int i = 0; i < specs.Count; i++)
            {
                if (rng.Value > mutationRate) continue;
                var spec = specs[i];
                float delta = (float)rng.Gaussian() * sigma * spec.Range;
                child.Set(spec.Key, spec.Clamp(Get(spec.Key) + delta));
                mutatedAny = true;
            }

            // Guarantee the child actually differs, otherwise the epoch is wasted.
            if (!mutatedAny)
            {
                var spec = specs[rng.Range(0, specs.Count)];
                child.Set(spec.Key, spec.Clamp(Get(spec.Key) + (float)rng.Gaussian() * sigma * spec.Range));
            }

            child.generation = generation + 1;
            child.lineage = "mutant g" + child.generation;
            return child;
        }

        static int Mathf_RoundToInt(float f)
        {
            return (int)Math.Round(f, MidpointRounding.AwayFromZero);
        }

        /// <summary>Mean absolute difference from another genome, in units of gene range.</summary>
        public float DistanceTo(StrategyGenome other)
        {
            var specs = Genes.All;
            if (specs.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < specs.Count; i++)
            {
                var s = specs[i];
                if (s.Range <= 0f) continue;
                sum += Math.Abs(Get(s.Key) - other.Get(s.Key)) / s.Range;
            }
            return sum / specs.Count;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref values, "values", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lineage, "lineage", "default");
            Scribe_Values.Look(ref generation, "generation", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars && values == null)
                values = new Dictionary<string, float>();
        }

        // ---- standalone XML, used by the cross-save archive -------------------------

        /// <summary>
        /// Serialises every known gene explicitly, including ones still sitting at their
        /// default.
        ///
        /// Writing only the genes that were explicitly Set() would archive an unmutated
        /// incumbent as an empty element — observed in a real colony, where eight epochs
        /// passed without a challenger being accepted and the stored strategy came out with
        /// zero genes in it. That reloads correctly only for as long as the defaults never
        /// change; the moment one does, every archived strategy silently becomes a different
        /// strategy. An archive is long-lived, so it has to say what it actually means.
        /// </summary>
        public XElement ToXml(string elementName)
        {
            var el = new XElement(elementName,
                new XAttribute("lineage", lineage ?? "default"),
                new XAttribute("generation", generation));

            var specs = Genes.All;
            for (int i = 0; i < specs.Count; i++)
            {
                var key = specs[i].Key;
                el.Add(new XElement("g",
                    new XAttribute("k", key),
                    new XAttribute("v", Get(key).ToString("R", CultureInfo.InvariantCulture))));
            }

            // Anything held that is no longer a registered gene (a mod was removed) is kept
            // verbatim, so re-adding that mod later restores its tuning rather than losing it.
            foreach (var kv in values)
            {
                if (Genes.Spec(kv.Key) != null) continue;
                el.Add(new XElement("g",
                    new XAttribute("k", kv.Key),
                    new XAttribute("v", kv.Value.ToString("R", CultureInfo.InvariantCulture))));
            }

            return el;
        }

        public static StrategyGenome FromXml(XElement el)
        {
            var g = new StrategyGenome();
            if (el == null) return g;
            var lin = el.Attribute("lineage");
            if (lin != null) g.lineage = lin.Value;
            var gen = el.Attribute("generation");
            int genVal;
            if (gen != null && int.TryParse(gen.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out genVal))
                g.generation = genVal;

            foreach (var ge in el.Elements("g"))
            {
                var k = ge.Attribute("k");
                var v = ge.Attribute("v");
                float f;
                if (k != null && v != null &&
                    float.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                {
                    g.values[k.Value] = f;
                }
            }
            return g;
        }

        /// <summary>Compact multi-line summary of genes that deviate from their defaults.</summary>
        public string Summarize(int maxLines)
        {
            var sb = new StringBuilder();
            int lines = 0;
            foreach (var spec in Genes.All)
            {
                if (lines >= maxLines) break;
                float v = Get(spec.Key);
                if (Math.Abs(v - spec.Default) < spec.Range * 0.02f) continue;
                sb.AppendLine(spec.Label + ": " + v.ToString("0.##", CultureInfo.InvariantCulture) +
                              "  (default " + spec.Default.ToString("0.##", CultureInfo.InvariantCulture) + ")");
                lines++;
            }
            if (lines == 0) sb.AppendLine("No genes have diverged from defaults yet.");
            return sb.ToString();
        }
    }
}
