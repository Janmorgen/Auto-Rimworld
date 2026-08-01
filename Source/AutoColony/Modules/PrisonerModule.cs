using System.Collections.Generic;
using AutoColony.Learning;
using AutoColony.Prisoners;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Treats a downed raider as an opportunity rather than a body.
    ///
    /// A prisoner is a colonist the colony did not have to grow — the cheapest population growth
    /// in the game, since raids arrive on their own. They can also be let go, or in the worst
    /// case killed. Nothing in the director took any of those options: downed hostiles were left
    /// where they fell to bleed out or crawl away.
    ///
    /// Two things blocked it structurally, and both had to go first. The planner would only
    /// consider a prison once the colony already held prisoners — and the game will not let
    /// anyone be captured without a prisoner bed to carry them to, so that was a deadlock with
    /// no way in. The prison's bed was also never marked for prisoners, so even a built one was
    /// just a bed.
    /// </summary>
    public class PrisonerModule : DirectorModule
    {
        public override string Name { get { return "Prisoners"; } }
        public override int IntervalTicks { get { return 2500; } }

        protected override void Act(DirectorContext ctx)
        {
            ApplyDispositions(ctx);
            TryCapture(ctx);
        }

        // ------------------------------------------------------------ capture

        /// <summary>
        /// Orders someone to carry a downed hostile to a prison bed.
        ///
        /// Given as a job rather than a designation because the game has no "capture" marker for
        /// the player to leave lying around — it is an ordered job in the vanilla UI too.
        /// </summary>
        void TryCapture(DirectorContext ctx)
        {
            // Never while the fight is still going. Walking an unarmed colonist into a firefight
            // to pick someone up is how a colony turns one casualty into two.
            if (ctx.state.hostilesNearBase > 0) return;

            var map = ctx.map;
            float recruitBias = ctx.Gene(Genes.ColonistRecruitBias);

            var downed = DownedHostiles(map);
            for (int i = 0; i < downed.Count; i++)
            {
                var victim = downed[i];
                if (victim.guest != null && victim.guest.IsPrisoner) continue;

                var bed = RestUtility.FindBedFor(victim, victim, false, false, GuestStatus.Prisoner);
                float value = ValueOf(victim);

                if (!PrisonerPolicy.WorthCapturing(value, ctx.state.daysOfFood, recruitBias,
                                                   bed != null, true))
                    continue;

                var carrier = NearestAbleColonist(ctx, victim);
                if (carrier == null) continue;

                var job = JobMaker.MakeJob(JobDefOf.Capture, victim, bed);
                job.count = 1;
                if (!carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc)) continue;

                Chronicle.Record(ChronicleCategory.Incident, string.Format(
                    "capturing {0} — worth {1:0.00} as a colonist, {2:0.0} days of food to keep them",
                    victim.LabelShortCap, value, ctx.state.daysOfFood));
                Note("sent " + carrier.LabelShortCap + " to capture " + victim.LabelShortCap);
                return;
            }
        }

        static List<Pawn> DownedHostiles(Map map)
        {
            var found = new List<Pawn>();
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn == null || pawn.Dead || !pawn.Downed) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Faction == Faction.OfPlayer) continue;
                if (!pawn.HostileTo(Faction.OfPlayer)) continue;
                found.Add(pawn);
            }
            return found;
        }

        static Pawn NearestAbleColonist(DirectorContext ctx, Pawn victim)
        {
            Pawn best = null;
            float bestDist = float.MaxValue;

            var able = ctx.state.ableColonists;
            for (int i = 0; i < able.Count; i++)
            {
                var pawn = able[i];
                if (pawn == null || pawn.Drafted) continue;
                if (pawn.WorkTagIsDisabled(WorkTags.Caring)) continue;
                if (!pawn.CanReach(victim, PathEndMode.OnCell, Danger.Some)) continue;

                float dist = (pawn.Position - victim.Position).LengthHorizontalSquared;
                if (dist < bestDist) { bestDist = dist; best = pawn; }
            }
            return best;
        }

        // ------------------------------------------------------------ what to do with them

        void ApplyDispositions(DirectorContext ctx)
        {
            if (ctx.state.prisoners == 0) return;

            float recruitBias = ctx.Gene(Genes.ColonistRecruitBias);
            var prisoners = ctx.map.mapPawns.PrisonersOfColony;

            for (int i = 0; i < prisoners.Count; i++)
            {
                var prisoner = prisoners[i];
                if (prisoner == null || prisoner.guest == null) continue;

                float value = ValueOf(prisoner);
                float resistance = prisoner.guest.resistance;

                var decision = PrisonerPolicy.Decide(
                    value, resistance, ctx.state.daysOfFood, recruitBias,
                    canRecruit: true,
                    executionAllowed: false);

                if (Apply(ctx, prisoner, decision, value, resistance)) return;
            }
        }

        /// <summary>
        /// Turns a decision into the game's own prisoner setting.
        ///
        /// Execution is deliberately never chosen. Every colonist who sees it takes a lasting
        /// mood penalty, and releasing costs nothing and achieves the same thing — a prisoner
        /// the colony does not want, gone. It stays in the policy because there are colonies it
        /// would suit, but this director is mood-sensitive enough that it would be paying for
        /// the privilege.
        /// </summary>
        bool Apply(DirectorContext ctx, Pawn prisoner, Disposition decision, float value,
                   float resistance)
        {
            PrisonerInteractionModeDef mode;
            switch (decision)
            {
                case Disposition.Recruit: mode = PrisonerInteractionModeDefOf.AttemptRecruit; break;
                case Disposition.Wear: mode = PrisonerInteractionModeDefOf.ReduceResistance; break;
                case Disposition.Release: mode = PrisonerInteractionModeDefOf.Release; break;
                case Disposition.Execute: mode = PrisonerInteractionModeDefOf.Execution; break;
                default: mode = PrisonerInteractionModeDefOf.MaintainOnly; break;
            }

            if (mode == null) return false;
            if (prisoner.guest.ExclusiveInteractionMode == mode) return false;
            if (prisoner.guest.IsInteractionDisabled(mode)) return false;

            prisoner.guest.SetExclusiveInteraction(mode);

            Chronicle.Record(ChronicleCategory.Incident, string.Format(
                "{0} with prisoner {1} — worth {2:0.00} as a colonist, resistance {3:0.0}, " +
                "{4:0.0} days of food",
                decision, prisoner.LabelShortCap, value, resistance, ctx.state.daysOfFood));
            Note(decision + " " + prisoner.LabelShortCap);
            return true;
        }

        // ------------------------------------------------------------ appraisal

        /// <summary>What this person would be worth as a colonist.</summary>
        public static float ValueOf(Pawn pawn)
        {
            if (pawn == null || pawn.skills == null) return 0f;

            int best = 0;
            float total = 0f;
            int counted = 0;

            var skills = pawn.skills.skills;
            for (int i = 0; i < skills.Count; i++)
            {
                var record = skills[i];
                if (record == null || record.TotallyDisabled) continue;
                if (record.Level > best) best = record.Level;
                total += record.Level;
                counted++;
            }

            float health = pawn.health != null && pawn.health.summaryHealth != null
                ? pawn.health.summaryHealth.SummaryHealthPercent
                : 1f;

            return PrisonerPolicy.Value(best, counted > 0 ? total / counted : 0f, health,
                                        pawn.WorkTagIsDisabled(WorkTags.Violent),
                                        counted == 0);
        }
    }
}
