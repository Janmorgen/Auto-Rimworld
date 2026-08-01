namespace AutoColony.Conditions
{
    /// <summary>
    /// Map-wide conditions in force right now, with no game types attached.
    ///
    /// RimWorld's conditions are not weather and not events: they last days, they change what
    /// the correct play *is* rather than merely making it harder, and several of them punish
    /// exactly the behaviour that is right the rest of the time. Toxic fallout poisons anything
    /// under open sky, so the hunting that normally answers hunger becomes the thing that kills
    /// the hunter. A solar flare turns off electricity outright, so a colony that has just
    /// finished wiring its stove has no stove.
    /// </summary>
    public struct ActiveConditions
    {
        public bool toxicFallout;
        public bool solarFlare;
        public bool eclipse;
        public bool coldSnap;
        public bool heatWave;
        public bool volcanicWinter;
        public bool flashstorm;

        public bool Any
        {
            get
            {
                return toxicFallout || solarFlare || eclipse || coldSnap ||
                       heatWave || volcanicWinter || flashstorm;
            }
        }
    }

    /// <summary>
    /// What a map-wide condition means the colony should do differently.
    ///
    /// Kept free of game types so the judgements can be tested offline, and kept separate from
    /// the modules that act on them because several modules need the same answer — the point of
    /// a condition is precisely that it changes more than one thing at once.
    /// </summary>
    public static class ConditionResponse
    {
        /// <summary>
        /// Whether going outside is itself the hazard.
        ///
        /// Toxic fallout accumulates in anything under open sky and kills without ever looking
        /// like combat. The director's usual answer to hunger is to send someone out after an
        /// animal, which during fallout is a way of poisoning the colonist and the meat at once.
        /// A flashstorm is the same shape: lightning strikes the open ground it is over.
        /// </summary>
        public static bool OutsideIsDangerous(ActiveConditions c)
        {
            return c.toxicFallout || c.flashstorm;
        }

        /// <summary>
        /// Whether elective outdoor work should stop.
        ///
        /// Elective is the operative word. Fighting a fire and answering a raid still happen
        /// outdoors, because those are not optional and refusing them costs more than the
        /// fallout does. What stops is gathering, hunting and fieldwork — the errands whose only
        /// justification is that they are usually worth it.
        /// </summary>
        public static bool SuspendElectiveOutdoorWork(ActiveConditions c)
        {
            return OutsideIsDangerous(c);
        }

        /// <summary>Whether electricity should be assumed gone rather than merely short.</summary>
        public static bool PowerIsOut(ActiveConditions c)
        {
            return c.solarFlare;
        }

        /// <summary>Whether anything solar should be assumed to produce nothing.</summary>
        public static bool NoSunlight(ActiveConditions c)
        {
            return c.eclipse || c.volcanicWinter;
        }

        /// <summary>
        /// Whether the fields are about to stop feeding anyone.
        ///
        /// Worth knowing separately from the food stock itself, because it is the one case where
        /// a full larder is not reassuring: the crops are the reason it will empty.
        /// </summary>
        public static bool CropsAtRisk(ActiveConditions c)
        {
            return c.toxicFallout || c.coldSnap || c.volcanicWinter;
        }

        /// <summary>One line naming what is in force, for the chronicle.</summary>
        public static string Describe(ActiveConditions c)
        {
            var sb = new System.Text.StringBuilder();
            Add(sb, c.toxicFallout, "toxic fallout");
            Add(sb, c.solarFlare, "solar flare");
            Add(sb, c.eclipse, "eclipse");
            Add(sb, c.coldSnap, "cold snap");
            Add(sb, c.heatWave, "heat wave");
            Add(sb, c.volcanicWinter, "volcanic winter");
            Add(sb, c.flashstorm, "flashstorm");
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }

        static void Add(System.Text.StringBuilder sb, bool active, string name)
        {
            if (!active) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(name);
        }
    }
}
