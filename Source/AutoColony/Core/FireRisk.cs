using AutoColony.Learning;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Prices fire rather than avoiding it.
    ///
    /// Wood is the right material most of the time: it is everywhere, it is cheap, and it goes
    /// up fast. Refusing it on principle costs a colony its early game. What actually matters
    /// is the environment — a damp boreal forest forgives a wooden base, an arid one in high
    /// summer does not — so the material choice is a reading of the conditions rather than a
    /// fixed preference.
    ///
    /// The same reading drives storage. Items left outdoors deteriorate, and items indoors
    /// behind wooden walls burn, so the answer in a dry climate is not "leave them out" but
    /// "get them into something that will not catch".
    /// </summary>
    public static class FireRisk
    {
        /// <summary>Risk at or above this is treated as a tinderbox.</summary>
        public const float HighRisk = 0.6f;

        /// <summary>
        /// Current fire risk, 0 to 1.
        ///
        /// Built on the game's own dryness and temperature model rather than a guess at biome
        /// behaviour, then adjusted for what is actually happening: rain suppresses fire, and
        /// something already burning is not a forecast but a fact.
        /// </summary>
        /// <summary>
        /// The risk a *permanent* decision should be made against.
        ///
        /// <see cref="Assess"/> is a reading of right now, and right now is the wrong timescale
        /// for choosing what a wall is made of. It multiplies the danger away by current
        /// rainfall, which is correct for "should somebody go and beat that fire out" and
        /// nonsense for "will this building be standing in a month" — rain today says nothing
        /// about next Jugust.
        ///
        /// Run 65 is the case. Seven rooms queued over six days on a permanent-summer map, and
        /// the recorded risk was 0.00 or 0.10 every single time, so the walls went up in wood
        /// and steel. On day 11 a raid started one fire that reached a hundred and thirty in
        /// nine hours at 36C and killed the colony. The director never saw a fire risk above a
        /// tenth on a map whose biome is called permanent summer.
        ///
        /// So the rain discount is dropped and the heat premium kept. Everything else — the
        /// game's own danger figure, live fires, raiders, unroofed power in the wet — still
        /// applies, because those are facts about the site rather than about the hour.
        /// </summary>
        public static float Lasting(Map map, ColonyState state)
        {
            float now = Assess(map, state);

            float baseline = 0.3f;
            if (map != null && map.fireWatcher != null)
                baseline = AcMath.Clamp01(map.fireWatcher.FireDanger);

            // Undo the weather. A wall outlives the shower that was falling when it was ordered.
            if (map != null && map.weatherManager != null)
            {
                float rain = AcMath.Clamp01(map.weatherManager.RainRate);
                if (rain > 0f) baseline = AcMath.Max(baseline, AcMath.Clamp01(baseline + rain * 0.5f));
            }

            // A map that runs hot is a map that burns, whatever today is doing.
            if (map != null && map.mapTemperature != null && map.mapTemperature.OutdoorTemp > 25f)
                baseline += 0.15f;

            return AcMath.Clamp01(AcMath.Max(now, baseline));
        }

        public static float Assess(Map map, ColonyState state)
        {
            if (map == null) return 0.3f;

            float risk = 0.3f;
            if (map.fireWatcher != null) risk = AcMath.Clamp01(map.fireWatcher.FireDanger);

            // Rain puts ordinary fires out and stops them starting.
            float rain = map.weatherManager != null ? AcMath.Clamp01(map.weatherManager.RainRate) : 0f;
            risk *= 1f - rain;

            // Heat dries everything out; the game's own danger figure already leans on this,
            // but a genuinely hot map deserves the extra nudge.
            if (map.mapTemperature != null && map.mapTemperature.OutdoorTemp > 30f)
                risk += 0.1f;

            if (state != null)
            {
                // Rain is not only protective. It is exactly what sets unroofed electrical
                // things alight: the game shorts them out, which starts a fire and, on a net
                // with charged batteries, an explosion. Treating rain as pure safety read the
                // risk as 0.00 during the very weather that caused seven fires in one test
                // colony — and the director lays its own conduit runs across open ground, so
                // this is a hazard it creates rather than one it merely encounters.
                if (rain > 0f && state.unroofedPowered > 0)
                    risk = AcMath.Max(risk, AcMath.Clamp01(rain * AcMath.Clamp01(state.unroofedPowered / 12f)));

                // A fire burning now is evidence, not a forecast.
                if (state.firesNearBase > 0) risk = AcMath.Max(risk, 0.9f);
                else if (state.fires > 0) risk = AcMath.Max(risk, 0.6f);

                // Raiders are the usual reason a base catches: they bring incendiaries and
                // they shoot things that burn.
                if (state.hostilesNearBase > 0) risk += 0.15f;
            }

            if (map.fireWatcher != null && map.fireWatcher.LargeFireDangerPresent)
                risk = AcMath.Max(risk, 0.85f);

            return AcMath.Clamp01(risk);
        }

        /// <summary>
        /// How strongly to prefer stone over wood right now, 0 to 1.
        ///
        /// The genome supplies a baseline taste and how much weight to give the environment;
        /// this combines them. A strategy can learn to be cavalier in a wet climate and
        /// cautious in a dry one without either being hardcoded.
        /// </summary>
        public static float StonePreference(DirectorContext ctx, float risk)
        {
            float baseline = ctx.Gene(Genes.BaseStonePreference);
            float aversion = ctx.Gene(Genes.FireRiskAversion);
            return AcMath.Clamp01(baseline + risk * aversion);
        }

        /// <summary>
        /// Storage leans harder toward stone than the rest of the base.
        ///
        /// It is where the colony's value accumulates, so it is the one room where a fire is
        /// not an inconvenience but the loss of everything that was worth hauling indoors.
        /// </summary>
        public static float StorageStonePreference(DirectorContext ctx, float risk)
        {
            return AcMath.Clamp01(StonePreference(ctx, risk) + 0.35f);
        }


    }
}
