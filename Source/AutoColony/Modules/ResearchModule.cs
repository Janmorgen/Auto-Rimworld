using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Keeps a research project selected at all times.
    ///
    /// Which project to pick is a discrete choice rather than a continuous parameter, so it is
    /// driven by a bandit instead of the genome: each project is an arm, and the arm pulled
    /// during an epoch is credited with that epoch's fitness. Because the bandit's statistics
    /// are also written to the cross-save archive, a colony that learns "get electricity early"
    /// passes that on to the next one.
    /// </summary>
    public class ResearchModule : DirectorModule
    {
        public const string BanditId = "research";

        public override string Name { get { return "Research"; } }
        public override int IntervalTicks { get { return 5000; } }

        readonly List<string> candidateKeys = new List<string>();
        readonly Dictionary<string, ResearchProjectDef> byKey = new Dictionary<string, ResearchProjectDef>();

        /// <summary>
        /// How many other projects each project unlocks, built once from the def database.
        ///
        /// Needed because a cold-start bandit has no opinion about anything, leaving cheapness
        /// as the only tiebreaker — which in vanilla opens the tech tree on a cosmetic dead end
        /// (observed in-game: the first project picked was ColoredLights). Counting dependents
        /// is a cheap stand-in for "is this foundational".
        /// </summary>
        static Dictionary<string, int> unlockCounts;
        static int maxUnlockCount = 1;

        static void EnsureUnlockCounts()
        {
            if (unlockCounts != null) return;
            unlockCounts = new Dictionary<string, int>();

            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var prereqs = all[i].prerequisites;
                if (prereqs == null) continue;
                for (int p = 0; p < prereqs.Count; p++)
                {
                    if (prereqs[p] == null) continue;
                    int n;
                    unlockCounts.TryGetValue(prereqs[p].defName, out n);
                    unlockCounts[prereqs[p].defName] = n + 1;
                }
            }

            maxUnlockCount = 1;
            foreach (var kv in unlockCounts)
                if (kv.Value > maxUnlockCount) maxUnlockCount = kv.Value;
        }

        static float UnlockScore(string defName)
        {
            int n;
            if (unlockCounts == null || !unlockCounts.TryGetValue(defName, out n)) return 0f;
            return n / (float)maxUnlockCount;
        }

        protected override void Act(DirectorContext ctx)
        {
            var rm = Find.ResearchManager;
            if (rm == null) return;

            var current = rm.GetProject();
            if (current != null && !current.IsFinished && current.CanStartNow) return;

            var pick = ChooseProject(ctx);
            if (pick == null) return;

            rm.SetCurrentProject(pick);
            ctx.Credit(BanditId, pick.defName);
            Note("started research '" + pick.defName + "'");
        }

        ResearchProjectDef ChooseProject(DirectorContext ctx)
        {
            candidateKeys.Clear();
            byKey.Clear();

            var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.IsFinished || !p.CanStartNow) continue;
                // Anomaly-style projects advance through a different mechanic entirely.
                if (p.knowledgeCategory != null) continue;

                candidateKeys.Add(p.defName);
                byKey[p.defName] = p;
            }

            if (candidateKeys.Count == 0) return null;

            EnsureUnlockCounts();

            var bandit = ctx.director.BanditFor(BanditId);
            float explore = ctx.Gene(Genes.ResearchExplore);
            float cheapBias = ctx.Gene(Genes.ResearchCheapBias);
            float unlockBias = ctx.Gene(Genes.ResearchUnlockBias);

            ResearchProjectDef best = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < candidateKeys.Count; i++)
            {
                var key = candidateKeys[i];
                var proj = byKey[key];

                // Learned value of this project, plus a prior with two parts: finish cheap
                // projects fast, but prefer ones the rest of the tree depends on. Cheapness
                // alone opens on whatever costs least, which is not the same as what helps.
                float cheapness = 1f / (1f + proj.baseCost / 2000f);
                float score = bandit.Score(key, explore)
                            + cheapBias * cheapness
                            + unlockBias * UnlockScore(key);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = proj;
                }
            }

            return best;
        }
    }
}
