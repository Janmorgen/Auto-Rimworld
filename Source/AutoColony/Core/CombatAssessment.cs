using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Judges whether a fight is winnable, and by whom.
    ///
    /// The naive version of this was a fixed danger ceiling, which is wrong in both directions.
    /// It stopped six armed colonists from taking a thrumbo they would comfortably have killed,
    /// and it would have let a colony starve rather than attempt the only meat on the map. What
    /// matters is not what the animal is, it is what *these* colonists are: their skills, their
    /// health, and what they are holding.
    ///
    /// It also has to account for desperation. A comfortable colony should refuse anything but
    /// a favourable fight. A starving one should take a bad fight, because losing the fight and
    /// losing the colony are no longer very different outcomes. And some threats are not
    /// elective at all — a raid or a fire is happening whether or not anyone suitable exists to
    /// answer it, so the question becomes who goes rather than whether.
    /// </summary>
    public static class CombatAssessment
    {
        /// <summary>Advantage insisted on when nothing is forcing the issue.</summary>
        const float ComfortableRatio = 2.0f;

        /// <summary>Advantage accepted when the alternative is losing the colony anyway.</summary>
        const float DesperateRatio = 0.5f;

        /// <summary>
        /// What one colonist is worth in a fight right now.
        ///
        /// Deliberately reads the weapon in their hands rather than their best skill: a
        /// brilliant shot holding nothing is not a brilliant shot, and the skill that counts is
        /// the one matching what they are actually carrying.
        /// </summary>
        public static float ColonistValue(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return 0f;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return 0f;

            int shooting = SkillLevel(pawn, SkillDefOf.Shooting);
            int melee = SkillLevel(pawn, SkillDefOf.Melee);

            var weapon = pawn.equipment != null ? pawn.equipment.Primary : null;

            float skill;
            float weaponFactor;
            if (weapon != null && weapon.def.IsRangedWeapon)
            {
                skill = shooting;
                weaponFactor = 1.35f;
            }
            else if (weapon != null && weapon.def.IsMeleeWeapon)
            {
                skill = melee;
                weaponFactor = 1.0f;
            }
            else
            {
                // Bare hands. Still counts for something, but not much.
                skill = melee;
                weaponFactor = 0.55f;
            }

            float health = 1f;
            if (pawn.health != null && pawn.health.summaryHealth != null)
                health = pawn.health.summaryHealth.SummaryHealthPercent;

            float able = (Capacity(pawn, PawnCapacityDefOf.Manipulation)
                        + Capacity(pawn, PawnCapacityDefOf.Moving)) * 0.5f;

            float value = (10f + skill * 5f) * weaponFactor * health * able;
            return value > 0f ? value : 0f;
        }

        /// <summary>Combined fighting value of everyone able to take the field.</summary>
        public static float ColonyStrength(ColonyState state)
        {
            if (state == null) return 0f;
            float total = 0f;
            for (int i = 0; i < state.ableColonists.Count; i++)
                total += ColonistValue(state.ableColonists[i]);
            return total;
        }

        /// <summary>
        /// How dangerous a hostile or wild animal is. Combat power is the game's own estimate;
        /// an already-wounded attacker is discounted for what it has lost.
        /// </summary>
        public static float ThreatValue(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return 0f;

            float power = pawn.kindDef != null ? pawn.kindDef.combatPower : 50f;
            if (power <= 0f) power = 50f;

            float health = 1f;
            if (pawn.health != null && pawn.health.summaryHealth != null)
                health = pawn.health.summaryHealth.SummaryHealthPercent;

            return power * AcMath.Clamp(health, 0.25f, 1f);
        }

        /// <summary>
        /// Whether to pick an elective fight.
        ///
        /// <paramref name="desperation"/> runs 0 (nothing is forcing this) to 1 (the
        /// alternative is losing the colony). At the top of that range almost any fight is
        /// accepted, because refusing is not actually the safe option.
        /// </summary>
        public static bool ShouldEngage(float colonyStrength, float threat, float desperation)
        {
            if (threat <= 0f) return true;
            float required = Lerp(ComfortableRatio, DesperateRatio, AcMath.Clamp(desperation, 0f, 1f));
            return colonyStrength >= threat * required;
        }

        /// <summary>
        /// Whether to start a fight with prey that will fight back.
        ///
        /// The judgement itself lives in <see cref="HuntPolicy"/>, where it can be argued with
        /// in a test — this file needs RimWorld types and so cannot be. Kept here as the entry
        /// point because every other combat judgement is.
        ///
        /// Twice now a cougar hunt has cost a colony. Run 36 took one at 1.57x and had a
        /// colonist mauled; run 56 declined the same animal twice at a 1.5x bar, then took it at
        /// 1.13x once hunger had lowered the bar to 1.1x, and lost two colonists to the revenge
        /// two days later — with the fight arriving when the colony was at 0.44x, because the
        /// first mauling had already put people on the floor.
        ///
        /// That last part is the reason a marginal ratio is worse than it looks: the hunt is
        /// judged at today's strength and the revenge arrives at tomorrow's.
        ///
        /// Genuine starvation still has its own door. <c>HuntPolicy.LastResortWarranted</c>
        /// takes the least dangerous animal on the map when nothing safe is left and desperation
        /// is past 0.85, which is the case this floor would otherwise strand.
        /// </summary>
        public static bool ShouldHuntDangerous(float colonyStrength, float threat, float desperation)
        {
            return HuntPolicy.WorthHunting(colonyStrength, threat, true, desperation, DesperateRatio);
        }

        /// <summary>
        /// Human-readable form of the same judgement, for the chronicle.
        ///
        /// The threat passed here is the largest animal the pass *declined*, and
        /// <c>ThreatOf</c> returns zero for anything that does not fight back — so a non-zero
        /// value means the bar that actually applied was the dangerous-prey floor, not the
        /// ordinary one. Printing the ordinary one anyway produced a line reporting "need 0.9x"
        /// beside a wolf refused at 0.97x, which is a diagnostic contradicting the decision it
        /// is describing. This project has lost enough hours to messages that named a cause
        /// nobody had checked.
        /// </summary>
        public static string Explain(float colonyStrength, float threat, float desperation)
        {
            float required = threat > 0f
                ? HuntPolicy.RequiredRatio(true, desperation, DesperateRatio)
                : Lerp(ComfortableRatio, DesperateRatio, AcMath.Clamp(desperation, 0f, 1f));
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "strength {0:0} vs threat {1:0}, need {2:0.0}x at desperation {3:0.00}",
                colonyStrength, threat, required, desperation);
        }

        /// <summary>
        /// Colonists ordered by how much use they will be, best first.
        ///
        /// Used to decide who fights rather than sending everyone: the point of ranking is that
        /// when only some need to go, the ones who go are the ones who can.
        /// </summary>
        public static List<Pawn> RankFighters(List<Pawn> candidates)
        {
            var ranked = new List<Pawn>();
            if (candidates == null) return ranked;

            for (int i = 0; i < candidates.Count; i++)
            {
                var pawn = candidates[i];
                if (pawn == null) continue;
                if (ColonistValue(pawn) <= 0f) continue;   // cannot or will not fight
                ranked.Add(pawn);
            }

            ranked.Sort((a, b) => ColonistValue(b).CompareTo(ColonistValue(a)));
            return ranked;
        }

        // ------------------------------------------------------------ helpers

        public static int SkillLevel(Pawn pawn, SkillDef def)
        {
            if (pawn.skills == null || def == null) return 0;
            var record = pawn.skills.GetSkill(def);
            return record != null ? record.Level : 0;
        }

        static float Capacity(Pawn pawn, PawnCapacityDef def)
        {
            try
            {
                if (pawn.health == null || pawn.health.capacities == null) return 1f;
                return AcMath.Clamp(pawn.health.capacities.GetLevel(def), 0f, 1f);
            }
            catch (System.Exception)
            {
                return 1f;
            }
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

    }
}
