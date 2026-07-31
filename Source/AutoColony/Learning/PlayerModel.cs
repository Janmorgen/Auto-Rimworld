using System.Collections.Generic;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>
    /// A strategy fitted to how the player actually runs their colony.
    ///
    /// The search is expensive: a colony affords only tens of epochs, against a strategy space
    /// of some fifty genes. Starting from defaults wastes most of that budget rediscovering
    /// things the player already demonstrated — how much food they keep, who they put on
    /// hauling, how many beds go in a room. Watching them play is close to free and produces a
    /// far better starting point than the defaults.
    ///
    /// Everything here is an equilibrium observation rather than a stated intention: what the
    /// colony holds and how work is assigned, averaged over many samples. That is a reasonable
    /// proxy for what the player was aiming at, and it is all that is observable.
    /// </summary>
    public class PlayerModel : IExposable
    {
        /// <summary>Observations needed before the model is trusted. One sample per in-game hour.</summary>
        public const int MinSamples = 150;

        public int samples;

        // Per-work-type emphasis: how widely and how urgently the player assigns each job.
        Dictionary<string, float> workEmphasis = new Dictionary<string, float>();

        // Stock equilibria.
        public float foodDaysSum;
        public float woodSum;
        public float steelSum;
        public float componentsSum;
        public float textilesSum;
        public float medicinePerColonistSum;

        // Layout.
        public float growCellsPerColonistSum;
        public float stockCellsPerColonistSum;
        public float bedsPerRoomSum;
        public float roomSizeSum;
        public int roomSamples;

        // Colonist policy.
        public float medCareSum;
        public float selfTendSum;
        public float recruitSum;
        public int recruitSamples;

        // Discrete preferences, counted rather than averaged.
        Dictionary<string, int> cropCounts = new Dictionary<string, int>();
        Dictionary<string, int> researchCounts = new Dictionary<string, int>();

        public bool IsUsable { get { return samples >= MinSamples; } }

        public float Progress { get { return samples / (float)MinSamples; } }

        public void AddWorkEmphasis(string workDefName, float emphasis)
        {
            float current;
            workEmphasis.TryGetValue(workDefName, out current);
            workEmphasis[workDefName] = current + emphasis;
        }

        public void CountCrop(string defName) { Bump(cropCounts, defName); }
        public void CountResearch(string defName) { Bump(researchCounts, defName); }

        static void Bump(Dictionary<string, int> counts, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int n;
            counts.TryGetValue(key, out n);
            counts[key] = n + 1;
        }

        static string Top(Dictionary<string, int> counts)
        {
            string best = null;
            int bestCount = 0;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            return best;
        }

        public string FavouriteCrop { get { return Top(cropCounts); } }
        public string FavouriteResearch { get { return Top(researchCounts); } }

        /// <summary>
        /// Converts the observations into a genome.
        ///
        /// Work weights are normalised against their own mean rather than used raw: the gene
        /// controls relative emphasis between work types, so what matters is that the player
        /// pushed hauling twice as hard as art, not the absolute numbers.
        /// </summary>
        public StrategyGenome ToGenome()
        {
            var genome = StrategyGenome.Default();
            if (samples == 0) return genome;

            genome.lineage = "fitted to player over " + samples + " observations";

            float n = samples;

            genome.Set(Genes.FoodDaysPerColonist, foodDaysSum / n);
            genome.Set(Genes.WoodTarget, woodSum / n);
            genome.Set(Genes.SteelTarget, steelSum / n);
            genome.Set(Genes.ComponentsTarget, componentsSum / n);
            genome.Set(Genes.TextilesTarget, textilesSum / n);
            genome.Set(Genes.MedicinePerColonist, medicinePerColonistSum / n);
            genome.Set(Genes.GrowingCellsPerColonist, growCellsPerColonistSum / n);
            genome.Set(Genes.StockpileCellsPerColonist, stockCellsPerColonistSum / n);
            genome.Set(Genes.ColonistMedCare, medCareSum / n);
            genome.Set(Genes.ColonistSelfTend, selfTendSum / n);

            if (recruitSamples > 0)
                genome.Set(Genes.ColonistRecruitBias, recruitSum / recruitSamples);

            if (roomSamples > 0)
            {
                genome.Set(Genes.BaseBedsPerRoom, bedsPerRoomSum / roomSamples);
                genome.Set(Genes.BaseRoomSize, roomSizeSum / roomSamples);
            }

            ApplyWorkWeights(genome);
            return genome;
        }

        void ApplyWorkWeights(StrategyGenome genome)
        {
            if (workEmphasis.Count == 0) return;

            float total = 0f;
            foreach (var kv in workEmphasis) total += kv.Value;
            float mean = total / workEmphasis.Count;
            if (mean <= 0.0001f) return;

            foreach (var kv in workEmphasis)
            {
                var key = Genes.WorkKey(kv.Key);
                if (Genes.Spec(key) == null) continue;   // work type from a mod no longer loaded
                genome.Set(key, kv.Value / mean);
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref samples, "samples", 0);
            Scribe_Collections.Look(ref workEmphasis, "workEmphasis", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref foodDaysSum, "foodDaysSum", 0f);
            Scribe_Values.Look(ref woodSum, "woodSum", 0f);
            Scribe_Values.Look(ref steelSum, "steelSum", 0f);
            Scribe_Values.Look(ref componentsSum, "componentsSum", 0f);
            Scribe_Values.Look(ref textilesSum, "textilesSum", 0f);
            Scribe_Values.Look(ref medicinePerColonistSum, "medicinePerColonistSum", 0f);
            Scribe_Values.Look(ref growCellsPerColonistSum, "growCellsPerColonistSum", 0f);
            Scribe_Values.Look(ref stockCellsPerColonistSum, "stockCellsPerColonistSum", 0f);
            Scribe_Values.Look(ref bedsPerRoomSum, "bedsPerRoomSum", 0f);
            Scribe_Values.Look(ref roomSizeSum, "roomSizeSum", 0f);
            Scribe_Values.Look(ref roomSamples, "roomSamples", 0);
            Scribe_Values.Look(ref medCareSum, "medCareSum", 0f);
            Scribe_Values.Look(ref selfTendSum, "selfTendSum", 0f);
            Scribe_Values.Look(ref recruitSum, "recruitSum", 0f);
            Scribe_Values.Look(ref recruitSamples, "recruitSamples", 0);
            Scribe_Collections.Look(ref cropCounts, "cropCounts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref researchCounts, "researchCounts", LookMode.Value, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (workEmphasis == null) workEmphasis = new Dictionary<string, float>();
                if (cropCounts == null) cropCounts = new Dictionary<string, int>();
                if (researchCounts == null) researchCounts = new Dictionary<string, int>();
            }
        }
    }
}
