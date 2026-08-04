using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Pins the world so two runs can be compared.
    ///
    /// Every run this project has ever driven started somewhere different. `-quicktest` calls
    /// <c>GenText.RandomSeedString</c> and <c>GameInitData.ChooseRandomStartingTile</c>, so the
    /// seed and the biome are both fresh each time — and that has quietly undermined most of the
    /// evidence gathered here. Run 109 was temperate and seasonal, 110 arid and permanently
    /// summer, 111 forested. When the colony did better or worse, there was no way to say whether
    /// the build had changed or the map had.
    ///
    /// It also hid a whole class of fault until it happened to land on the right map. The wood
    /// chain was wrong from the first day of this project and only surfaced on run 110, because
    /// that was the first arid start — the fires had always been fed by a forest nobody had to
    /// think about. A fixed matrix of biomes finds that on purpose instead of by luck.
    ///
    /// Two environment variables, both optional and both inert when unset:
    ///
    ///   AUTOCOLONY_BIOME=BorealForest   the starting tile's biome, by defName
    ///   AUTOCOLONY_SEED=arid-01         the world seed string
    ///
    /// Seed is held apart from biome on purpose. Holding the seed and varying the biome asks
    /// "how does the director cope with this terrain"; holding both and varying the build asks
    /// "did this change help", which is the question a run is usually meant to answer and the
    /// one that was never actually being asked.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TestBed
    {
        public static string Biome { get { return Env("AUTOCOLONY_BIOME"); } }
        public static string Seed { get { return Env("AUTOCOLONY_SEED"); } }

        static string Env(string key)
        {
            try
            {
                var value = Environment.GetEnvironmentVariable(key);
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch (Exception) { return null; }
        }

        static TestBed()
        {
            if (Biome == null && Seed == null) return;

            try
            {
                var harmony = new Harmony("autocolony.testbed");
                harmony.PatchAll(typeof(TestBed).Assembly);

                Log.Message("[AutoColony] test bed active — biome " + (Biome ?? "any") +
                            ", seed " + (Seed ?? "random"));
            }
            catch (Exception e)
            {
                Log.Error("[AutoColony] test bed failed to patch: " + e);
            }
        }

        /// <summary>
        /// A tile in the wanted biome, chosen deterministically.
        ///
        /// The *first* valid tile in index order rather than a random one, because a random pick
        /// inside a fixed biome would put the seed back where it started. Same seed and same
        /// biome must give the same tile, or nothing above is worth anything.
        /// </summary>
        public static bool TryFindTile(string biomeDefName, out PlanetTile found)
        {
            found = PlanetTile.Invalid;

            var wanted = DefDatabase<BiomeDef>.GetNamedSilentFail(biomeDefName);
            if (wanted == null)
            {
                Log.Error("[AutoColony] no biome named '" + biomeDefName + "' — " +
                          "known biomes: " + KnownBiomes());
                return false;
            }

            var grid = Find.WorldGrid;
            if (grid == null) return false;

            var reason = new StringBuilder();
            for (int i = 0; i < grid.TilesCount; i++)
            {
                var tile = new PlanetTile(i);
                Tile data;
                try { data = grid[tile]; }
                catch (Exception) { continue; }

                if (data == null || data.PrimaryBiome != wanted) continue;

                reason.Length = 0;
                if (!TileFinder.IsValidTileForNewSettlement(tile, reason, false)) continue;

                found = tile;
                return true;
            }

            Log.Error("[AutoColony] no settleable " + biomeDefName + " tile on this world — " +
                      "try another seed");
            return false;
        }

        static string KnownBiomes()
        {
            var names = new List<string>();
            var all = DefDatabase<BiomeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].canBuildBase) names.Add(all[i].defName);
            return string.Join(", ", names.ToArray());
        }
    }

    /// <summary>
    /// The world seed, when one is asked for.
    ///
    /// Patched at <c>RandomSeedString</c> rather than at the generator, because that is the one
    /// place quicktest decides it and patching there leaves ordinary play untouched.
    /// </summary>
    [HarmonyPatch(typeof(GenText), "RandomSeedString")]
    public static class Patch_FixedSeed
    {
        static void Postfix(ref string __result)
        {
            var seed = TestBed.Seed;
            if (seed != null) __result = seed;
        }
    }

    /// <summary>
    /// The starting tile, when a biome is asked for.
    ///
    /// A postfix rather than a prefix: the game's own choice runs first and stands as the
    /// fallback, so a biome that does not exist on this world leaves a playable colony rather
    /// than a broken one. A test bed that can brick the run is worse than no test bed.
    /// </summary>
    [HarmonyPatch(typeof(GameInitData), "ChooseRandomStartingTile")]
    public static class Patch_FixedBiome
    {
        static void Postfix(GameInitData __instance)
        {
            var biome = TestBed.Biome;
            if (biome == null) return;

            PlanetTile tile;
            if (!TestBed.TryFindTile(biome, out tile)) return;

            __instance.startingTile = tile;
            Log.Message("[AutoColony] starting tile pinned to " + biome + " at " + tile);
        }
    }
}
