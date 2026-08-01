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

        // Prisoners used to be handled here too, as a straight recruit-or-not read off the
        // genome. They now belong to PrisonerModule, which weighs the person against the
        // colony's situation — and two modules writing the same setting on alternate passes
        // would simply have fought each other.
        protected override void Act(DirectorContext ctx)
        {
            ApplyColonistSettings(ctx);
        }

        void ApplyColonistSettings(DirectorContext ctx)
        {
            var care = (MedicalCareCategory)AcMath.Clamp(ctx.GeneInt(Genes.ColonistMedCare), 0, 4);
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

    }
}
