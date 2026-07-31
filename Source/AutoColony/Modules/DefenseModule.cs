using System.Collections.Generic;
using AutoColony.Learning;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Handles threats: builds static defenses in peacetime and takes manual control of
    /// colonists when a raid arrives.
    ///
    /// Two evolvable trade-offs live here. How much wealth to sink into turrets is a real
    /// cost — over-building starves the economy, under-building loses colonists — and how
    /// eagerly to draft matters because a drafted colonist does no work, so a jumpy strategy
    /// quietly bleeds productivity all year to survive a handful of raids.
    /// </summary>
    public class DefenseModule : DirectorModule
    {
        public override string Name { get { return "Defense"; } }

        // Checked often: a raid landing needs a response in game-minutes, not game-hours.
        public override int IntervalTicks { get { return 600; } }

        readonly List<Pawn> drafted = new List<Pawn>();

        /// <summary>
        /// Fortification scans hundreds of cells for a legal turret spot, which is far too
        /// expensive to repeat at the module's combat-response cadence. Only every Nth quiet
        /// pass looks for somewhere to build.
        /// </summary>
        const int FortifyEveryNPasses = 16;
        int quietPasses;

        protected override void Act(DirectorContext ctx)
        {
            if (ThreatActive(ctx))
            {
                HandleThreat(ctx);
                return;
            }

            StandDown(ctx);

            if (++quietPasses >= FortifyEveryNPasses)
            {
                quietPasses = 0;
                MaintainDefenses(ctx);
            }
        }

        bool ThreatActive(DirectorContext ctx)
        {
            var danger = ctx.state.danger;
            if (danger == StoryDanger.None) return false;

            // 0 = never draft, 1 = only for serious raids, 2 = react to anything.
            float threshold = ctx.Gene(Genes.DefenseDraftDanger);
            if (threshold < 0.5f) return false;
            if (danger == StoryDanger.Low && threshold < 1.5f) return false;
            return ctx.state.hostilePawns > 0;
        }

        // ------------------------------------------------------------ combat

        void HandleThreat(DirectorContext ctx)
        {
            var rally = RallyPoint(ctx);
            float retreatAt = ctx.Gene(Genes.DefenseRetreatHealth);
            int mobilised = 0;

            for (int i = 0; i < ctx.state.ableColonists.Count; i++)
            {
                var pawn = ctx.state.ableColonists[i];
                if (pawn.drafter == null) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Violent)) continue;

                float health = pawn.health != null && pawn.health.summaryHealth != null
                    ? pawn.health.summaryHealth.SummaryHealthPercent
                    : 1f;

                // Too hurt to fight: release them so they seek treatment instead.
                if (health < retreatAt)
                {
                    if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
                    drafted.Remove(pawn);
                    continue;
                }

                if (!pawn.drafter.Drafted)
                {
                    pawn.drafter.Drafted = true;
                    if (!drafted.Contains(pawn)) drafted.Add(pawn);
                    mobilised++;
                }

                SendToRally(pawn, rally, ctx.map);
            }

            if (mobilised > 0) Note("drafted " + mobilised + " colonists against a threat");
        }

        static void SendToRally(Pawn pawn, IntVec3 rally, Map map)
        {
            if (!rally.IsValid) return;
            if (pawn.CurJobDef == JobDefOf.Goto) return;
            // Already close enough to be useful; let the pawn pick its own targets.
            if ((pawn.Position - rally).LengthHorizontalSquared <= 36) return;

            var cell = CellFinder.RandomClosewalkCellNear(rally, map, 3);
            if (!cell.IsValid || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        void StandDown(DirectorContext ctx)
        {
            if (drafted.Count == 0) return;

            for (int i = 0; i < drafted.Count; i++)
            {
                var pawn = drafted[i];
                if (pawn == null || pawn.Dead || pawn.drafter == null) continue;
                if (pawn.drafter.Drafted) pawn.drafter.Drafted = false;
            }

            Note("stood down " + drafted.Count + " colonists");
            drafted.Clear();
        }

        /// <summary>Colonists fall back to the base entrance rather than meeting raiders in the open.</summary>
        static IntVec3 RallyPoint(DirectorContext ctx)
        {
            if (ctx.layout != null && ctx.layout.established) return ctx.layout.origin;
            return ctx.map.Center;
        }

        // ------------------------------------------------------------ fortification

        void MaintainDefenses(DirectorContext ctx)
        {
            var turretDef = AcDefs.TurretMini;
            if (turretDef == null) return;

            // Threat scales with wealth, so the defense budget does too.
            float budgetFraction = ctx.Gene(Genes.DefenseWealthRatio);
            int wanted = ctx.GeneInt(Genes.DefenseTurretCount);
            int affordable = (int)(ctx.state.wealthTotal * budgetFraction / 300f);
            if (wanted > affordable) wanted = affordable;
            if (wanted <= ctx.state.turrets) return;

            if (!CanAfford(ctx, turretDef)) return;

            var origin = ctx.layout.established ? ctx.layout.origin : ctx.map.Center;
            var stuff = PlacementUtil.ChooseStuff(ctx.map, turretDef, 0f);

            foreach (var cell in GenRadial.RadialCellsAround(origin, 14, true))
            {
                if ((cell - origin).LengthHorizontalSquared < 25) continue;   // keep a standoff distance
                if (!cell.InBounds(ctx.map)) continue;

                if (PlacementUtil.TryPlace(ctx.map, turretDef, cell, Rot4.North, stuff))
                {
                    PlacementUtil.MarkHome(ctx.map, cell);
                    Note("queued a turret at " + cell);
                    return;
                }
            }
        }

        static bool CanAfford(DirectorContext ctx, ThingDef def)
        {
            var costs = def.CostList;
            if (costs == null) return true;

            for (int i = 0; i < costs.Count; i++)
            {
                var need = costs[i];
                if (need.thingDef == null) continue;
                if (ctx.map.resourceCounter.GetCount(need.thingDef) < need.count * 2) return false;
            }
            return true;
        }
    }
}
