namespace AutoColony
{
    /// <summary>
    /// Who is not sent to the fight once somebody is already on the ground.
    ///
    /// A downed colonist does not die of the wound. They die of nobody arriving: untended, they
    /// bleed out, and unfed they starve where they fell. Colonies were lost this way with days
    /// of food in the stockpile, because every able body had been drafted into the same fight
    /// that put the others down and there was nobody left standing to carry any of it.
    ///
    /// So a casualty changes the arithmetic of a raid. Answering it is still not optional — the
    /// raid is happening — but the last person able to treat the wounded is worth more holding
    /// a medicine kit than holding a rifle they were never going to turn the fight with.
    /// </summary>
    public static class CasualtyPolicy
    {
        /// <summary>
        /// Whether to keep one colonist out of the fighting to tend the wounded.
        ///
        /// Only ever one, and only while somebody is actually down: two held back would be
        /// giving away the fight, and holding anyone back in a colony with nobody to hold back
        /// for is the jumpiness the draft genes exist to avoid. It also refuses to empty the
        /// line entirely — a lone colonist facing a raid does not get to opt out of it, because
        /// losing the fight and losing the colony are the same outcome at that point.
        ///
        /// That last clause is right about a fight and wrong about a clock. Runs 132, 134 and
        /// 135 all ended the same way: three colonists on the floor, one still upright, and the
        /// rule refusing to release them because releasing them would empty the line. Run 135,
        /// day 4, with twenty-five medicine in store and four beds standing empty:
        ///
        ///   06h  nobody down any more; Radya rejoins the line
        ///   06h  3 down and no bed would go up — laid a sleeping spot
        ///   06h  WITHDRAWING 1 — strength 96 vs threat 180 (0.54x), needed 4.50x
        ///   09h  died of Blood loss (extreme) — Celia
        ///   10h  died of Blood loss (extreme) — Keng
        ///
        /// The line it was kept in was a withdrawal it had already decided on. It stood in a
        /// refuge for four hours while the people it could have saved bled to death.
        ///
        /// So bleeding is the thing that overrides it, rather than merely being down. Somebody
        /// down can wait; somebody bleeding out dies on a clock measured in hours whatever the
        /// fight does, and one colonist is not turning a fight the colony has already priced at
        /// half the advantage it wanted.
        /// </summary>
        public static bool ShouldReserveMedic(int ableFighters, int downedColonists, int bleedingOut)
        {
            if (downedColonists <= 0) return false;
            if (ableFighters >= 2) return true;
            return bleedingOut > 0;
        }

        /// <summary>
        /// How much better the odds have to be before the few still standing meet a threat in
        /// the open, given how many are already down.
        ///
        /// Desperation scales acceptable risk upward; this is the same idea running the other
        /// way. A colony with three of four on the floor is one lost fight from having nobody
        /// left to tend, feed or carry anyone, and that is not a survivable position however the
        /// fight itself looks — where withdrawing risks only what the raider can do to a
        /// defended room. So the stake, not just the odds, belongs in the decision.
        ///
        /// Watched happen: with three colonists down, the director drafted the fourth against a
        /// single raider on a 95-to-77 advantage, lost her, and the whole colony bled out over
        /// the next four hours with eleven days of food in the store. That fight was worth
        /// having on its numbers and not worth having on its stake.
        ///
        /// This raises the bar rather than forbidding the fight. Answering a threat is never
        /// optional — the raider comes either way — so what changes is whether it is met outside
        /// or from cover.
        /// </summary>
        public static float EngagementCaution(int ableFighters, int downedColonists)
        {
            return EngagementCaution(ableFighters, downedColonists, 0f);
        }

        /// <summary>
        /// The same, told how much a colony this small should fear losing one.
        ///
        /// The two-argument form is 1.0 until somebody is already down, so a colony of three
        /// demanded exactly the margin a colony of twelve demanded. The fights that end colonies
        /// begin with nobody down: by the time the casualty term rises, three are on the floor
        /// and the arithmetic has arrived after the event it was meant to prevent.
        ///
        /// Losing one of three costs a third of the labour, a third of the defence, and a third
        /// of whoever can tend the other two. Losing one of ten costs a tenth. The stake differs
        /// enormously and the bar could not say so — which is #51 seen from the engagement side,
        /// and what twenty-two blood-loss deaths across eight colonies in one session were
        /// mostly made of.
        ///
        /// Scarcity is one over the hands available, so it falls away naturally as a colony
        /// grows and needs no threshold anywhere. How much it is worth is the genome's to argue
        /// with, because how badly a colony should fear being small is a strategy rather than a
        /// fact.
        /// </summary>
        public static float EngagementCaution(int ableFighters, int downedColonists,
                                              float scarcityWeight)
        {
            if (ableFighters <= 0) return 1f;

            float caution = 1f;
            if (downedColonists > 0) caution += (float)downedColonists / ableFighters;

            if (scarcityWeight > 0f) caution += scarcityWeight / ableFighters;

            return caution;
        }

        /// <summary>
        /// The advantage worth having before abandoning a defensible position.
        ///
        /// Half again the enemy's strength. A doorway is worth a great deal — raiders have to
        /// come through it one at a time, into prepared fire — and giving that up for an even
        /// fight is a poor trade whatever the raw numbers say.
        /// </summary>
        public const float MinimumToLeaveCover = 1.5f;

        /// <summary>
        /// The advantage the colony insists on before meeting a threat in the open.
        ///
        /// Answering a raid is never optional, but meeting it *outside* is — and it is elective
        /// exactly when there is somewhere better to meet it. Hunting has always been judged
        /// this way, a comfortable colony demanding roughly two to one before an elective fight;
        /// defence was judged on a bare gene whose default lets a colony charge a threat three
        /// times its strength. One did, at 0.68 against a bar of 0.35, and three of its four
        /// colonists were on the floor within the hour.
        ///
        /// With nowhere to withdraw to the gene stands unaltered, because a colony with no walls
        /// yet is not choosing between two options — the fight is coming to it either way.
        /// </summary>
        /// The floor is applied first and the casualty multiplier on top of it, not the other way
        /// round. Taking the larger of the two would let the floor swallow the multiplier — a
        /// colony with three of four down would demand no more than one with nobody down, which
        /// is the opposite of the point.
        /// <summary>
        /// The advantage worth having when a casualty cannot be recovered from.
        ///
        /// Lower than the cover floor, because there is genuinely nowhere better to fight — but
        /// well clear of parity, because an even fight costs somebody and this colony has no way
        /// to get them back.
        /// </summary>
        public const float MinimumWithoutMedicalCapacity = 1.25f;

        /// The floor is applied first and the casualty multiplier on top of it, not the other way
        /// round. Taking the larger of the two would let the floor swallow the multiplier — a
        /// colony with three of four down would demand no more than one with nobody down, which
        /// is the opposite of the point.
        public static float RequiredAdvantage(float geneRatio, int ableFighters,
                                              int downedColonists, bool hasRefuge,
                                              bool canRecoverCasualties)
        {
            return RequiredAdvantage(geneRatio, ableFighters, downedColonists, hasRefuge,
                                     canRecoverCasualties, 0f);
        }

        /// <summary>The same, with the genome's view of how much being few is worth fearing.</summary>
        public static float RequiredAdvantage(float geneRatio, int ableFighters,
                                              int downedColonists, bool hasRefuge,
                                              bool canRecoverCasualties, float scarcityWeight)
        {
            float required = geneRatio;

            if (hasRefuge && required < MinimumToLeaveCover)
            {
                required = MinimumToLeaveCover;
            }
            else if (!canRecoverCasualties && required < MinimumWithoutMedicalCapacity)
            {
                // No walls to hold *and* nowhere to carry a wounded colonist. The old rule read
                // "no refuge" as "no choice" and dropped the bar to the raw gene — so the colony
                // that could least afford a casualty fought at the loosest bar it would ever
                // have, which inverts the whole point of weighing the stake.
                //
                // Across twenty-seven observed colonies the split is clean: every early fight
                // taken at 0.68x, 0.96x, 1.08x or 1.09x was followed by severe colonist loss,
                // while those taken at 1.59x and above were not, and the colony that only ever
                // engaged at 3.02x was the healthiest of the run.
                required = MinimumWithoutMedicalCapacity;
            }

            return required * EngagementCaution(ableFighters, downedColonists, scarcityWeight);
        }
    }
}
