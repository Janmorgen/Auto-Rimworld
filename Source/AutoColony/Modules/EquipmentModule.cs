using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutoColony.Modules
{
    /// <summary>
    /// Puts the right weapon in the right hands.
    ///
    /// A colonist's fighting value is mostly decided before the fight starts, by what they are
    /// holding and whether it suits them. A crack shot with nothing is not a crack shot, and a
    /// brawler holding a rifle is worse than a brawler holding a knife — the game raises an
    /// alert about exactly that, which the director previously had no way to act on.
    ///
    /// So each colonist is matched to the best weapon available for the way *they* fight:
    /// shooters get guns, brawlers and melee specialists get blades, and anyone empty-handed
    /// gets whatever is lying in the stockpile rather than nothing.
    /// </summary>
    public class EquipmentModule : DirectorModule
    {
        public override string Name { get { return "Equipment"; } }
        public override int IntervalTicks { get { return 10000; } }

        readonly List<Thing> available = new List<Thing>();

        protected override void Act(DirectorContext ctx)
        {
            CollectAvailableWeapons(ctx.map);
            if (available.Count == 0) return;

            int rearmed = 0;
            for (int i = 0; i < ctx.state.ableColonists.Count; i++)
            {
                if (TryImproveWeapon(ctx, ctx.state.ableColonists[i])) rearmed++;
            }

            if (rearmed > 0) Note("sent " + rearmed + " colonists to pick up better weapons");
        }

        void CollectAvailableWeapons(Map map)
        {
            available.Clear();
            var weapons = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);

            for (int i = 0; i < weapons.Count; i++)
            {
                var weapon = weapons[i];
                if (weapon == null || !weapon.Spawned || weapon.Destroyed) continue;
                if (weapon.IsForbidden(Faction.OfPlayer)) continue;
                // Something already carried is not on the floor to be picked up.
                if (weapon.ParentHolder is Pawn_EquipmentTracker) continue;
                if (!weapon.def.IsWeapon) continue;

                available.Add(weapon);
            }
        }

        bool TryImproveWeapon(DirectorContext ctx, Pawn pawn)
        {
            if (pawn == null || pawn.equipment == null) return false;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;
            if (pawn.Drafted) return false;                     // mid-fight is no time to swap
            if (pawn.CurJobDef == JobDefOf.Equip) return false;  // already on the way

            bool preferMelee = PrefersMelee(pawn);
            var current = pawn.equipment.Primary;
            float currentScore = current != null ? ScoreFor(pawn, current, preferMelee) : 0f;

            Thing best = null;
            float bestScore = currentScore;

            for (int i = 0; i < available.Count; i++)
            {
                var candidate = available[i];
                if (!pawn.CanReserveAndReach(candidate, PathEndMode.OnCell, Danger.Deadly)) continue;

                float score = ScoreFor(pawn, candidate, preferMelee);
                if (score > bestScore * 1.15f)   // only move for a clear improvement
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best == null) return false;

            var job = JobMaker.MakeJob(JobDefOf.Equip, best);
            if (!pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) return false;

            available.Remove(best);
            Chronicle.Record(ChronicleCategory.Health, string.Format(
                "{0} sent to equip {1} ({2} fighter{3})",
                pawn.LabelShort, best.LabelCap,
                preferMelee ? "melee" : "ranged",
                current != null ? ", replacing " + current.LabelCap : ", was unarmed"));
            return true;
        }

        /// <summary>
        /// Which way a colonist actually fights. A brawler is a special case rather than a
        /// close call: they take a mood hit and lose accuracy holding a gun, so their skills
        /// do not get a vote.
        /// </summary>
        static bool PrefersMelee(Pawn pawn)
        {
            if (pawn.story != null && pawn.story.traits != null &&
                pawn.story.traits.HasTrait(TraitDefOf.Brawler)) return true;

            int shooting = CombatAssessment.SkillLevel(pawn, SkillDefOf.Shooting);
            int melee = CombatAssessment.SkillLevel(pawn, SkillDefOf.Melee);
            return melee > shooting + 2;   // needs a real margin, guns win ties
        }

        /// <summary>
        /// How good a weapon is for this particular colonist. Market value stands in for raw
        /// quality — better weapons cost more — and the type multiplier is what makes the
        /// answer personal rather than a global ranking.
        /// </summary>
        static float ScoreFor(Pawn pawn, Thing weapon, bool preferMelee)
        {
            var def = weapon.def;
            if (!def.IsRangedWeapon && !def.IsMeleeWeapon) return 0f;

            float quality;
            try { quality = weapon.MarketValue; }
            catch (System.Exception) { quality = 0f; }
            if (quality <= 0f) quality = 10f;

            bool ranged = def.IsRangedWeapon;
            float fit = preferMelee
                ? (ranged ? 0.25f : 1f)
                : (ranged ? 1f : 0.45f);

            return quality * fit;
        }
    }
}
