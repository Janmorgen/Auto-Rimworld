using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony.Modules
{
    /// <summary>
    /// Applies the per-colonist settings a player would otherwise tweak by hand: medical care
    /// level, self-tending, how to react to hostiles, and what to do with prisoners.
    ///
    /// Medicine policy is a genuine economic choice — glitterworld medicine on every scratch
    /// wins fights and loses winters — so the care level is a gene rather than a constant.
    /// </summary>
    public class ColonistPolicyModule : DirectorModule
    {
        public override string Name { get { return "Colonist policy"; } }
        public override int IntervalTicks { get { return 20000; } }

        protected override void Act(DirectorContext ctx)
        {
            ApplyColonistSettings(ctx);
            ApplyPrisonerPolicy(ctx);
        }

        void ApplyColonistSettings(DirectorContext ctx)
        {
            var care = (MedicalCareCategory)Clamp(ctx.GeneInt(Genes.ColonistMedCare), 0, 4);
            bool selfTend = ctx.Gene(Genes.ColonistSelfTend) >= 0.5f;
            int changed = 0;

            for (int i = 0; i < ctx.state.allColonists.Count; i++)
            {
                var pawn = ctx.state.allColonists[i];
                var ps = pawn.playerSettings;
                if (ps == null) continue;

                if (ps.medCare != care)
                {
                    ps.medCare = care;
                    changed++;
                }

                if (ps.selfTend != selfTend)
                {
                    ps.selfTend = selfTend;
                    changed++;
                }

                // Non-combatants should run rather than trade shots they cannot win.
                var wanted = pawn.WorkTagIsDisabled(WorkTags.Violent)
                    ? HostilityResponseMode.Flee
                    : HostilityResponseMode.Attack;

                if (ps.hostilityResponse != wanted)
                {
                    ps.hostilityResponse = wanted;
                    changed++;
                }
            }

            if (changed > 0) Note("updated policy on " + changed + " settings");
        }

        void ApplyPrisonerPolicy(DirectorContext ctx)
        {
            if (ctx.state.prisoners == 0) return;

            float recruitBias = ctx.Gene(Genes.ColonistRecruitBias);
            var mode = recruitBias >= 0.5f
                ? PrisonerInteractionModeDefOf.AttemptRecruit
                : PrisonerInteractionModeDefOf.MaintainOnly;

            var prisoners = ctx.map.mapPawns.PrisonersOfColony;
            int changed = 0;

            for (int i = 0; i < prisoners.Count; i++)
            {
                var p = prisoners[i];
                if (p == null || p.guest == null) continue;
                if (p.guest.ExclusiveInteractionMode == mode) continue;

                p.guest.SetExclusiveInteraction(mode);
                changed++;
            }

            if (changed > 0) Note("set " + changed + " prisoners to " + mode.defName);
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
