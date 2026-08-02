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

        /// <summary>None of this is worth doing while the colony is burning.</summary>
        public override bool Discretionary { get { return true; } }

        protected override void Act(DirectorContext ctx)
        {
            ApplyDispositions(ctx);

            // The colony's own casualties come before a stranger's.
            //
            // Capturing and rescuing both issue an *ordered* job, which overrides whatever the
            // carrier would otherwise have chosen — including tending the colonist lying on the
            // floor at home. Watched live, in the same in-game hour that Sierrap died of their
            // wounds: "rescuing Walrus, who is not hostile — no prison needed and they may well
            // stay". The last able colonist was sent across the map for a stranger while two of
            // their own bled out with free beds and medicine waiting.
            //
            // A recruit is worth having. It is not worth the two people already here.
            if (ctx.state.colonistsDowned == 0) TryCapture(ctx);
        }

        // ------------------------------------------------------------ capture

        /// <summary>
        /// Picks up a downed stranger, by whichever route their hostility allows.
        ///
        /// The two are not variations on one action. A hostile can only be *captured*, into a
        /// prisoner bed the colony had to build beforehand, and then fed and worked on
        /// indefinitely. Anyone not hostile — a pod crash survivor, a wanderer, a visitor caught
        /// in someone else's fight — can be *rescued* into an ordinary bed instead, which needs
        /// no prison at all and generally ends far better: they often join outright, and where
        /// they have a faction it buys goodwill with it rather than a grudge.
        ///
        /// Both are given as ordered jobs rather than designations, because the game has no
        /// marker for either — they are ordered jobs in the vanilla UI too.
        /// </summary>
        void TryCapture(DirectorContext ctx)
        {

            float recruitBias = ctx.Gene(Genes.ColonistRecruitBias);
            var downed = DownedStrangers(ctx.map);

            for (int i = 0; i < downed.Count; i++)
            {
                var victim = downed[i];
                if (victim.guest != null && victim.guest.IsPrisoner) continue;
                if (victim.InBed()) continue;      // already picked up

                bool hostile = victim.HostileTo(Faction.OfPlayer);
                if (Collect(ctx, victim, hostile, recruitBias)) return;
            }
        }

        bool Collect(DirectorContext ctx, Pawn victim, bool hostile, float recruitBias)
        {
            Building_Bed bed;
            JobDef job;
            string what;

            // The carrier has to be found first, because the bed is looked up *on their behalf*.
            // Asking as the victim returns nothing: a hostile pawn cannot claim one of the
            // colony's beds under their own name, which reads identically to owning no prison.
            var carrier = NearestAbleColonist(ctx, victim);
            if (carrier == null)
            {
                Decline(victim, "nobody free and able could reach them");
                return false;
            }

            if (hostile)
            {
                bed = RestUtility.FindBedFor(victim, carrier, false, false, GuestStatus.Prisoner);
                float value = ValueOf(victim);
                if (!PrisonerPolicy.WorthCapturing(value, ctx.state.daysOfFood, recruitBias,
                                                   bed != null, true))
                {
                    // Say why, once. A capture that silently does not happen is indistinguishable
                    // from a module that is not running, and three separate causes — no prison
                    // bed, no food, nobody worth taking — look identical from outside.
                    Decline(victim, string.Format(
                        "no capture: bed {0}, worth {1:0.00}, food {2:0.0} days, appetite {3:0.00}",
                        bed != null ? "found" : "NONE", value, ctx.state.daysOfFood, recruitBias));
                    return false;
                }

                job = JobDefOf.Capture;
                what = string.Format("capturing {0} — worth {1:0.00} as a colonist, {2:0.0} days " +
                                     "of food to keep them", victim.LabelShortCap, value,
                                     ctx.state.daysOfFood);
            }
            else
            {
                // An ordinary bed: rescue needs no prison, which is most of why it is the better
                // outcome when the option exists at all.
                bed = RestUtility.FindBedFor(victim, carrier, false, false, null);
                if (!PrisonerPolicy.WorthRescuing(ctx.state.daysOfFood, bed != null, true))
                    return false;

                job = JobDefOf.Rescue;
                what = "rescuing " + victim.LabelShortCap + ", who is not hostile — no prison " +
                       "needed and they may well stay";
            }

            var ordered = JobMaker.MakeJob(job, victim, bed);
            ordered.count = 1;
            if (!carrier.jobs.TryTakeOrderedJob(ordered, JobTag.Misc)) return false;

            Chronicle.Record(ChronicleCategory.Incident, what);
            Note(carrier.LabelShortCap + " sent for " + victim.LabelShortCap);
            return true;
        }

        readonly Dictionary<int, string> declined = new Dictionary<int, string>();

        /// <summary>Records a refusal once per person per reason, so it explains without spamming.</summary>
        void Decline(Pawn victim, string why)
        {
            string previous;
            if (declined.TryGetValue(victim.thingIDNumber, out previous) && previous == why) return;

            declined[victim.thingIDNumber] = why;
            Chronicle.Record(ChronicleCategory.Incident,
                "left " + victim.LabelShortCap + " where they fell — " + why);
        }

        /// <summary>
        /// Every downed outsider on the map, hostile or not.
        ///
        /// Not raid-specific: a downed stranger is a downed stranger however they arrived, and a
        /// crashed transport pod puts one on the tile with no raid anywhere in sight.
        /// </summary>
        static List<Pawn> DownedStrangers(Map map)
        {
            var found = new List<Pawn>();
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn == null || pawn.Dead || !pawn.Downed) continue;
                if (!pawn.RaceProps.Humanlike) continue;
                if (pawn.Faction == Faction.OfPlayer) continue;
                found.Add(pawn);
            }

            // Anyone who can simply be rescued comes first: it is cheaper, it needs no prison,
            // and it is the one that goes away if they bleed out while a warden is deliberating.
            found.Sort(delegate(Pawn a, Pawn b)
            {
                int ah = a.HostileTo(Faction.OfPlayer) ? 1 : 0;
                int bh = b.HostileTo(Faction.OfPlayer) ? 1 : 0;
                return ah.CompareTo(bh);
            });
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
