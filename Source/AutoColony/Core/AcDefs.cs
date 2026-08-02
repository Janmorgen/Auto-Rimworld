using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AutoColony
{
    /// <summary>
    /// Null-tolerant def lookups.
    ///
    /// The director must keep running on any mod list and any DLC combination, so every def
    /// it touches is resolved by name and allowed to be missing. Anything that resolves to
    /// null simply disables the feature that needed it instead of throwing inside a tick.
    /// </summary>
    public static class AcDefs
    {
        static readonly Dictionary<string, ThingDef> thingCache = new Dictionary<string, ThingDef>();
        static readonly Dictionary<string, RecipeDef> recipeCache = new Dictionary<string, RecipeDef>();

        public static ThingDef Thing(string defName)
        {
            ThingDef d;
            if (thingCache.TryGetValue(defName, out d)) return d;
            d = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            thingCache[defName] = d;
            return d;
        }

        public static RecipeDef Recipe(string defName)
        {
            RecipeDef d;
            if (recipeCache.TryGetValue(defName, out d)) return d;
            d = DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
            recipeCache[defName] = d;
            return d;
        }

        public static ThingDef Cloth { get { return Thing("Cloth"); } }
        public static ThingDef Fire { get { return Thing("Fire"); } }
        public static ThingDef Wall { get { return Thing("Wall"); } }
        public static ThingDef Door { get { return Thing("Door"); } }
        public static ThingDef Bed { get { return Thing("Bed"); } }

        /// <summary>
        /// A patch of floor designated for sleeping. Free, instant, and a real bed as far as the
        /// game is concerned — which is the only property that matters when somebody is bleeding
        /// on the ground and a rescue needs somewhere to carry them.
        /// </summary>
        public static ThingDef SleepingSpot { get { return Thing("SleepingSpot"); } }
        public static ThingDef Sandbag { get { return Thing("Sandbags"); } }
        public static ThingDef TurretMini { get { return Thing("Turret_MiniTurret"); } }
        public static ThingDef Battery { get { return Thing("Battery"); } }
        public static ThingDef SolarGenerator { get { return Thing("SolarGenerator"); } }
        public static ThingDef WoodFiredGenerator { get { return Thing("WoodFiredGenerator"); } }
        public static ThingDef ElectricStove { get { return Thing("ElectricStove"); } }
        public static ThingDef FueledStove { get { return Thing("FueledStove"); } }
        public static ThingDef Campfire { get { return Thing("Campfire"); } }
        public static ThingDef ButcherTable { get { return Thing("TableButcher"); } }
        public static ThingDef CraftingSpot { get { return Thing("CraftingSpot"); } }
        public static ThingDef ResearchBench { get { return Thing("SimpleResearchBench"); } }
        public static ThingDef StonecuttersTable { get { return Thing("TableStonecutter"); } }
        public static ThingDef Torch { get { return Thing("TorchLamp"); } }
        public static ThingDef PsychiteTea { get { return Thing("PsychiteTea"); } }
        public static ThingDef Cooler { get { return Thing("Cooler"); } }

        /// <summary>
        /// Somewhere to sew. The hand bench needs no power, which matters because clothing is
        /// most urgently wanted in exactly the early colony that has no grid yet; the electric
        /// one is only preferred once there is electricity to run it.
        /// </summary>
        public static ThingDef TailorBench
        {
            get
            {
                var electric = Thing("ElectricTailoringBench");
                if (electric != null && PlacementUtil.ResearchDone(electric)) return electric;
                return Thing("HandTailoringBench");
            }
        }
        public static ThingDef PowerConduit { get { return Thing("PowerConduit"); } }
        public static ThingDef Grave { get { return Thing("Grave"); } }
        public static ThingDef Heater { get { return Thing("Heater"); } }

        /// <summary>Stuff candidates for walls/furniture, cheapest and most available first.</summary>
        public static readonly string[] WoodyStuff = { "WoodLog" };
        public static readonly string[] StoneBlockStuff =
        {
            "BlocksGranite", "BlocksLimestone", "BlocksSandstone", "BlocksSlate", "BlocksMarble"
        };
        public static readonly string[] MetalStuff = { "Steel" };
    }
}
