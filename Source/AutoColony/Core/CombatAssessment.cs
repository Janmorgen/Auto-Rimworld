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
            return FightingValue(pawn);
        }

        /// <summary>
        /// What a pawn is worth in a fight — the same question for a colonist and a raider.
        ///
        /// The old version read (10 + skill x 5) x a flat weapon factor, which knew whether a
        /// weapon was ranged or melee and nothing else. A pawn with a bolt-action rifle and one
        /// with a revolver scored identically; so did a pawn in plate armour and one in a shirt.
        /// On the other side of the fight it was worse: raiders were scored on kindDef.combatPower
        /// alone, an average for their *type*, so what they were actually carrying and wearing
        /// never entered the comparison the colony used to decide whether to fight them.
        ///
        /// Now both sides are read the same way, from what they are holding and wearing:
        ///
        ///   offence      what the weapon in their hands actually does per second
        ///   accuracy     how often they land it, from the skill that matches the weapon
        ///   toughness    health, working limbs, and armour
        ///
        /// Passion is deliberately absent. It governs how fast a skill grows, not how well the
        /// pawn shoots today, so it belongs to the question of who to train rather than to what
        /// this fight is worth — see ColonistPotential.
        /// </summary>
        public static float FightingValue(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return 0f;

            float offence = Offence(pawn);
            float toughness = Toughness(pawn);
            float value = offence * toughness;
            return value > 0f ? value : 0f;
        }

        /// <summary>
        /// Damage per second with the weapon actually held, times the chance of landing it.
        ///
        /// Melee reads the game's own MeleeDPS stat, which already folds in the weapon, its
        /// quality, the pawn's skill and their manipulation. Ranged has no equivalent stat, so it
        /// is computed the way the game computes it: projectile damage over the full shot cycle,
        /// scaled by the pawn's shooting accuracy.
        /// </summary>
        static float Offence(Pawn pawn)
        {
            var weapon = pawn.equipment != null ? pawn.equipment.Primary : null;

            if (weapon != null && weapon.def.IsRangedWeapon)
            {
                float dps = RangedDps(weapon);
                float accuracy = Stat(pawn, StatDefOf.ShootingAccuracyPawn, 0.6f);
                // Accuracy is per-cell and compounds over distance; a straight multiply is the
                // honest simplification, and it is the same one on both sides of the fight.
                return dps * AcMath.Clamp(accuracy, 0.05f, 1f) * 10f;
            }

            // Melee, or bare hands — MeleeDPS answers both, and answers unarmed correctly rather
            // than with a guessed penalty.
            float melee = Stat(pawn, StatDefOf.MeleeDPS, 2f);
            return melee * 10f;
        }

        /// <summary>
        /// Projectile damage across the whole shot cycle: burst size over warmup plus cooldown.
        ///
        /// A weapon that fires three rounds a burst and takes two seconds to cycle is not the
        /// same as one that fires once and cycles in one, and the old ranged/melee flag could not
        /// tell them apart.
        /// </summary>
        static float RangedDps(Thing weapon)
        {
            try
            {
                var verbs = weapon.def.Verbs;
                if (verbs == null || verbs.Count == 0) return 3f;

                var v = verbs[0];
                var projectile = v.defaultProjectile;
                if (projectile == null || projectile.projectile == null) return 3f;

                float damage = projectile.projectile.GetDamageAmount(weapon);
                float shots = v.burstShotCount > 0 ? v.burstShotCount : 1;
                float cycle = v.warmupTime + v.defaultCooldownTime;
                if (cycle <= 0.01f) cycle = 1f;

                return damage * shots / cycle;
            }
            catch (System.Exception) { return 3f; }
        }

        /// <summary>
        /// How much punishment they can take: health, working limbs, and what they are wearing.
        ///
        /// Armour is the piece that was missing entirely. Sharp and blunt ratings run 0 to about
        /// 1 for the best gear a pre-industrial colony sees, and each point roughly halves what
        /// gets through — so it is worth as much as a second colonist and was worth nothing at
        /// all in the old number.
        /// </summary>
        static float Toughness(Pawn pawn)
        {
            float health = 1f;
            if (pawn.health != null && pawn.health.summaryHealth != null)
                health = pawn.health.summaryHealth.SummaryHealthPercent;

            float able = (Capacity(pawn, PawnCapacityDefOf.Manipulation)
                        + Capacity(pawn, PawnCapacityDefOf.Moving)) * 0.5f;

            float sharp = Stat(pawn, StatDefOf.ArmorRating_Sharp, 0f);
            float blunt = Stat(pawn, StatDefOf.ArmorRating_Blunt, 0f);
            float armour = 1f + AcMath.Clamp(sharp + blunt, 0f, 2f) * 0.75f;

            return AcMath.Clamp(health, 0.1f, 1f) * AcMath.Clamp(able, 0.1f, 1f) * armour;
        }

        static float Stat(Pawn pawn, StatDef def, float fallback)
        {
            try { return def != null ? pawn.GetStatValue(def) : fallback; }
            catch (System.Exception) { return fallback; }
        }

        /// <summary>
        /// How much better this colonist could get at fighting, which is where passion belongs.
        ///
        /// Passion does not make a pawn shoot straighter today — it multiplies the experience
        /// they gain, so it says who is worth training when the colony is under-matched. A Major
        /// passion at skill 4 will overtake a None passion at skill 8, and that is a fact about
        /// next month rather than about this raid.
        /// </summary>
        public static float ColonistPotential(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return 0f;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent)) return 0f;

            var weapon = pawn.equipment != null ? pawn.equipment.Primary : null;
            var def = weapon != null && weapon.def.IsRangedWeapon
                ? SkillDefOf.Shooting : SkillDefOf.Melee;

            try
            {
                var skill = pawn.skills != null ? pawn.skills.GetSkill(def) : null;
                if (skill == null || skill.TotallyDisabled) return 0f;

                float passion = skill.passion == Passion.Major ? 2f
                              : skill.passion == Passion.Minor ? 1.5f : 1f;
                // Room left to grow matters as much as the rate of growing.
                float headroom = AcMath.Clamp((20 - skill.Level) / 20f, 0f, 1f);
                return passion * headroom;
            }
            catch (System.Exception) { return 0f; }
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
        /// Combined fighting value of a named group — the force that will actually walk out,
        /// as opposed to the roster it was drawn from.
        ///
        /// <see cref="ColonyStrength"/> answers "how strong is this colony", which is the right
        /// question for readiness and the wrong one for a fight, because the people it counts
        /// include the medic held back and anyone too hurt to stand. Those two questions were
        /// the same call for a long time and the difference killed a colony.
        /// </summary>
        public static float StrengthOf(List<Pawn> pawns)
        {
            if (pawns == null) return 0f;
            float total = 0f;
            for (int i = 0; i < pawns.Count; i++) total += ColonistValue(pawns[i]);
            return total;
        }

        /// <summary>
        /// How dangerous a hostile or wild animal is. Combat power is the game's own estimate;
        /// an already-wounded attacker is discounted for what it has lost.
        /// </summary>
        public static float ThreatValue(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return 0f;

            // A humanlike attacker is read exactly as a colonist is, because they are the same
            // kind of thing: a body with a weapon and some armour on it. kindDef.combatPower is
            // an average for their *type*, so a tribal with a bolt-action and a tribal with a
            // club scored the same, and the colony compared its real strength against their
            // notional one.
            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike)
            {
                float measured = FightingValue(pawn);
                if (measured > 0f) return measured;
            }

            // Animals carry nothing, so the game's own estimate for the kind is the right number.
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
