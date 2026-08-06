using System.Collections.Generic;

namespace AutoColony
{
    /// <summary>
    /// What a hunt costs in revenge, counted over the whole hunting session rather than one
    /// animal at a time.
    ///
    /// Run 161 hunted muffalo and was answered with four Muffalo revenges in eight days, two
    /// colonists downed. Nothing was misweighted. The colony asked the right question at the
    /// wrong level, twice over:
    ///
    /// **The def is a chance per wound, and it was read as a chance per hunt.** The field is
    /// called manhunterOnDamageChance and docs/rimworld/animals.md describes it as "the odds a
    /// wounded animal turns and attacks the hunter" — per wounding, not per animal. A muffalo
    /// has healthScale 1.75 against a rat's 0.29, so it absorbs several times the shooting
    /// before it drops, and every one of those shots is another roll. Ten percent per wound
    /// across seven wounds is a shade over fifty percent per hunt, which is a different animal
    /// from the one the ten percent described.
    ///
    /// **The risk is bought as a set and was judged one animal at a time.** Day 3 designated
    /// five muffalo in a single pass, each judged on its own as a comfortable 514 against 100.
    /// Five hunts that each turn half the time is a ninety-seven percent chance that something
    /// turns. Every individual decision was defensible and the pass as a whole was not.
    ///
    /// Same shape as the wrong-scope row in goal.md's table — a colony's want tallied once per
    /// room — one level along: a session's risk tallied once per animal.
    ///
    /// This class only measures. Whether the risk is worth taking stays where it was, in
    /// <see cref="HuntPolicy.WorthHunting"/>, which already knows how to weigh a fight against
    /// colony strength and hunger. It was being handed the wrong number.
    ///
    /// Free of game types so the arithmetic can be argued with in a test.
    /// </summary>
    public static class HuntRisk
    {
        /// <summary>
        /// How many wounding hits it takes to bring an animal down, from how much damage it
        /// absorbs.
        ///
        /// A stand-in for the shooting itself, which depends on the weapon, the range, the
        /// hunter's skill and the cover — none of which are known when the designation is made.
        /// What is known is that a bigger animal takes more shots, and it is the count of shots
        /// that matters here because each one is another roll against the revenge chance.
        /// </summary>
        public static float WoundsToFell(float healthScale, float woundsPerHealth)
        {
            if (healthScale <= 0f) healthScale = 0.1f;
            if (woundsPerHealth < 1f) woundsPerHealth = 1f;

            float wounds = healthScale * woundsPerHealth;
            return wounds < 1f ? 1f : wounds;
        }

        /// <summary>
        /// The chance this one hunt ends with the animal coming for the hunter.
        ///
        /// One minus the chance every wound in turn fails to provoke it. A per-wound chance of
        /// zero stays zero however long the hunt takes, which is what makes deer and rats free
        /// to hunt in any number — the arithmetic says so rather than a clause exempting them.
        /// </summary>
        public static float RevengeChance(float chancePerWound, float wounds)
        {
            if (chancePerWound <= 0f) return 0f;
            if (chancePerWound >= 1f) return 1f;
            if (wounds < 1f) wounds = 1f;

            float survives = 1f;
            float miss = 1f - chancePerWound;

            // Integer part by repeated multiplication, remainder by interpolation: a power
            // function is not available to this assembly and the loop is over single digits.
            int whole = (int)wounds;
            if (whole > 64) whole = 64;
            for (int i = 0; i < whole; i++) survives *= miss;

            float rest = wounds - whole;
            if (rest > 0f) survives *= 1f - chancePerWound * rest;

            return 1f - survives;
        }

        /// <summary>
        /// The chance that anything at all in this set of hunts turns.
        ///
        /// Not used to decide anything — <see cref="ExpectedRetaliation"/> is what the bar
        /// judges — but it is the honest headline for the chronicle, and it is the number that
        /// makes the fault legible: five hunts at fifty percent each is not a fifty percent
        /// evening.
        /// </summary>
        public static float AnyRevenge(List<float> chances)
        {
            if (chances == null || chances.Count == 0) return 0f;

            float none = 1f;
            for (int i = 0; i < chances.Count; i++)
            {
                float c = chances[i];
                if (c <= 0f) continue;
                if (c >= 1f) return 1f;
                none *= 1f - c;
            }
            return 1f - none;
        }

        /// <summary>
        /// The fighting power this set of hunts is expected to turn on the colony.
        ///
        /// Each hunt contributes its own threat weighted by how likely it is to come back, so
        /// one warg at certainty counts fully and a dozen rats count for nothing. This is the
        /// number <see cref="HuntPolicy.WorthHunting"/> should be judging, in place of the
        /// threat of whichever animal happens to be under consideration.
        /// </summary>
        public static float ExpectedRetaliation(List<float> chances, List<float> threats)
        {
            if (chances == null || threats == null) return 0f;

            float total = 0f;
            int n = chances.Count < threats.Count ? chances.Count : threats.Count;
            for (int i = 0; i < n; i++)
            {
                if (chances[i] <= 0f || threats[i] <= 0f) continue;
                total += chances[i] * threats[i];
            }
            return total;
        }
    }
}
