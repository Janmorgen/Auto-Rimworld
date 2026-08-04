using System;
using RimWorld;
using Verse;

namespace AutoColony.Furniture
{
    /// <summary>
    /// Whether the colony can keep another fire fed.
    ///
    /// Run 110 reached day 12 at 41C with a heatstroke alert, a colonist needing treatment, and
    /// seven dry hoppers — among them the passive coolers that were the entire answer to the
    /// heat. Hauling was already at the top of the work table. The colony was not ignoring the
    /// problem; it did not have the hands.
    ///
    /// And the director had spent those days adding more things that burn wood. Every passive
    /// cooler, torch lamp and campfire it placed drew from the same woodpile and the same
    /// hauling hours as the stove, so each new one made the existing ones *less* likely to be
    /// fed. That is the shape this project is meant to design against: an action that is locally
    /// correct — the room is too hot, place a cooler — and makes the colony worse, with nothing
    /// in the structure to notice.
    ///
    /// The rule is a labour statement rather than a threshold. When the game says more hoppers
    /// want filling than there are colonists to fill them, the colony is behind, and one more
    /// fire is one more thing that will stand empty. Nothing here knows what a cooler is.
    /// </summary>
    public static class FuelUpkeep
    {
        /// <summary>Anything that has to be carried wood before it will work.</summary>
        public static bool Burns(ThingDef def)
        {
            return def != null && def.HasComp(typeof(CompRefuelable));
        }

        /// <summary>
        /// More hoppers wanting fuel than colonists to carry it.
        ///
        /// <c>buildingsWantingFuel</c> is the game's own <c>ShouldAutoRefuelNow</c> summed over
        /// the colony, so this is not an opinion about how empty is empty — it is the count of
        /// jobs RimWorld is currently waiting for somebody to take.
        /// </summary>
        public static bool BehindOnFuel(ColonyState state)
        {
            if (state == null) return false;
            return FuelBudget.BehindOnFuel(state.buildingsWantingFuel, state.colonists);
        }

        /// <summary>
        /// Whether placing one more of this def is something the colony can carry.
        ///
        /// The first of a kind always passes. A colony with no cooler at all in a heatwave has a
        /// problem that a cooler solves, and refusing it because the stove is empty would trade
        /// one emergency for another. What this stops is the *second* and the fifth — the ones
        /// that divide the same wood and the same hours further.
        /// </summary>
        public static bool CanKeepAnotherFed(ColonyState state, Map map, ThingDef def)
        {
            if (!Burns(def)) return true;
            if (state == null) return true;
            return FuelBudget.CanKeepAnotherFed(state.buildingsWantingFuel, state.colonists,
                                                CountOn(map, def));
        }

        /// <summary>
        /// Why the colony said no, for the chronicle. A refusal nobody can read is the same
        /// problem as a measurement nobody takes.
        /// </summary>
        public static string Refusal(ColonyState state, ThingDef def)
        {
            return string.Format(
                "not placing another {0} — {1} of the colony's fires are already waiting on wood " +
                "and there are {2} colonists to carry it; another one burns the same woodpile and " +
                "the same hours",
                def != null ? (def.label ?? def.defName) : "burner",
                state != null ? state.buildingsWantingFuel : 0,
                state != null ? state.colonists : 0);
        }

        static int CountOn(Map map, ThingDef def)
        {
            if (map == null || def == null || map.listerThings == null) return 0;
            try
            {
                var things = map.listerThings.ThingsOfDef(def);
                return things != null ? things.Count : 0;
            }
            catch (Exception) { return 0; }
        }
    }
}
