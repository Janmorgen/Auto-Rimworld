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
        public static float Assess(Map map, ColonyState state)
        {
            if (map == null) return 0.3f;

            float risk = 0.3f;
            if (map.fireWatcher != null) risk = Clamp01(map.fireWatcher.FireDanger);

            // Rain puts ordinary fires out and stops them starting.
            float rain = map.weatherManager != null ? Clamp01(map.weatherManager.RainRate) : 0f;
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
                    risk = Max(risk, Clamp01(rain * Clamp01(state.unroofedPowered / 12f)));

                // A fire burning now is evidence, not a forecast.
                if (state.firesNearBase > 0) risk = Max(risk, 0.9f);
                else if (state.fires > 0) risk = Max(risk, 0.6f);

                // Raiders are the usual reason a base catches: they bring incendiaries and
                // they shoot things that burn.
                if (state.hostilesNearBase > 0) risk += 0.15f;
            }

            if (map.fireWatcher != null && map.fireWatcher.LargeFireDangerPresent)
                risk = Max(risk, 0.85f);

            return Clamp01(risk);
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
            return Clamp01(baseline + risk * aversion);
        }

        /// <summary>
        /// Storage leans harder toward stone than the rest of the base.
        ///
        /// It is where the colony's value accumulates, so it is the one room where a fire is
        /// not an inconvenience but the loss of everything that was worth hauling indoors.
        /// </summary>
        public static float StorageStonePreference(DirectorContext ctx, float risk)
        {
            return Clamp01(StonePreference(ctx, risk) + 0.35f);
        }

        static float Max(float a, float b) { return a > b ? a : b; }

        static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
