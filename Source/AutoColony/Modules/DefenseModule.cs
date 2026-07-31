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
            // Fire first, and before anything else. It spreads far faster than the work
            // scheduler reconsiders priorities, and an unattended fire will take a base apart
            // while the director is still deciding who should be hauling.
            if (ctx.state.fires > 0) HandleFires(ctx);

            if (ThreatActive(ctx))
            {
                HandleThreat(ctx);
                return;
            }

            StandDown(ctx);
            if (ctx.state.fires == 0 && firefightingUnderway)
            {
                firefightingUnderway = false;
                Chronicle.Record(ChronicleCategory.Fire, "fires are out");
            }

            if (++quietPasses >= FortifyEveryNPasses)
            {
                quietPasses = 0;
                MaintainDefenses(ctx);
            }
        }

        /// <summary>
        /// Whether to take manual control against hostiles on the map.
        ///
        /// A single raider registers as <see cref="StoryDanger.Low"/>, and this used to ignore
        /// Low outright at the default setting. That is how a lone raider walked into a colony,
        /// set it on fire and killed most of it while the director stood by: low danger to the
        /// storyteller is not low danger to a wooden base. Any hostile now draws a response
        /// unless the strategy has been tuned never to draft.
        /// </summary>
        bool ThreatActive(DirectorContext ctx)
        {
            if (ctx.state.hostilePawns <= 0) return false;

            float willingness = ctx.Gene(Genes.DefenseDraftDanger);
            if (willingness < 0.5f) return false;   // a strategy that never takes control
            if (willingness >= 1.5f) return true;   // answer anything hostile, anywhere

            // The middle band answers what the storyteller calls serious, and anything that
            // has actually reached the colony — which is what matters for arson.
            return ctx.state.danger == StoryDanger.High || HostilesNearBase(ctx);
        }

        /// <summary>Combined strength of everything hostile currently on the map.</summary>
        static float HostileStrength(DirectorContext ctx)
        {
            float total = 0f;
            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                total += CombatAssessment.ThreatValue(p);
            }
            return total;
        }

        static bool HostilesNearBase(DirectorContext ctx)
        {
            var origin = ctx.layout != null && ctx.layout.established ? ctx.layout.origin : ctx.map.Center;
            const int NearSq = 45 * 45;

            var pawns = ctx.map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                if (p == null || p.Dead || !p.HostileTo(Faction.OfPlayer)) continue;
                if ((p.Position - origin).LengthHorizontalSquared <= NearSq) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ fire

        bool firefightingUnderway;

        /// <summary>
        /// Gets colonists onto a fire.
        ///
        /// Two things stop them by default. Colonists only fight fires inside the home area,
        /// so a fire started at the edge of a base — or by a raider outside it — burns
        /// unopposed; the home area is extended to cover it. And work priorities are only
        /// reconsidered every few in-game hours, which is far too slow, so the work module is
        /// pushed to re-run immediately rather than waiting for its turn.
        /// </summary>
        void HandleFires(DirectorContext ctx)
        {
            var map = ctx.map;
            var fireDef = AcDefs.Fire;
            if (fireDef == null) return;

            var home = map.areaManager.Home;
            var fires = map.listerThings.ThingsOfDef(fireDef);
            int claimed = 0;

            for (int i = 0; i < fires.Count && claimed < 200; i++)
            {
                var fire = fires[i];
                if (fire == null || !fire.Spawned) continue;

                // Claim the fire and a ring around it, so the whole burning front is inside
                // the area colonists are willing to work in.
                foreach (var cell in GenRadial.RadialCellsAround(fire.Position, 4f, true))
                {
                    if (!cell.InBounds(map)) continue;
                    if (home != null && !home[cell]) { home[cell] = true; claimed++; }
                }
            }

            if (!firefightingUnderway)
            {
                firefightingUnderway = true;
                ctx.director.ForceModuleDue("Work priorities");
                Note("fire detected — claimed " + claimed + " cells and re-prioritised work");
                Chronicle.Record(ChronicleCategory.Fire, string.Format(
                    "{0} fires burning; claimed {1} cells into the home area and forced a work re-prioritisation",
                    ctx.state.fires, claimed));
            }
        }

        // ------------------------------------------------------------ combat

        void HandleThreat(DirectorContext ctx)
        {
            var rally = RallyPoint(ctx);
            float retreatAt = ctx.Gene(Genes.DefenseRetreatHealth);
            int mobilised = 0;

            // Best fighters first. A raid is not elective — it is happening regardless of
            // whether anyone suitable exists — so the question is who goes, not whether.
            var fighters = CombatAssessment.RankFighters(ctx.state.ableColonists);

            for (int i = 0; i < fighters.Count; i++)
            {
                var pawn = fighters[i];
                if (pawn.drafter == null) continue;

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

            if (mobilised > 0)
            {
                Note("drafted " + mobilised + " colonists against a threat");
                Chronicle.Record(ChronicleCategory.Threat, string.Format(
                    "{0} hostiles (danger {1}); drafted {2} to {3} — {4}",
                    ctx.state.hostilePawns, ctx.state.danger, mobilised, rally,
                    CombatAssessment.Explain(CombatAssessment.ColonyStrength(ctx.state),
                                             HostileStrength(ctx), 1f)));
            }
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
            Chronicle.Record(ChronicleCategory.Threat, "threat over; stood down " + drafted.Count + " colonists");
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
