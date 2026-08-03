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

        static readonly Dictionary<string, RoomStatDef> roomStatCache =
            new Dictionary<string, RoomStatDef>();

        /// <summary>
        /// A room stat by name, for the ones <c>RoomStatDefOf</c> does not carry.
        ///
        /// RimWorld defines eleven room stats and exposes only the five visible ones as static
        /// fields. The hidden six are the interesting ones — they are what cleanliness and
        /// impressiveness actually *do* — so reaching them needs the database.
        /// </summary>
        public static RoomStatDef RoomStat(string defName)
        {
            RoomStatDef d;
            if (roomStatCache.TryGetValue(defName, out d)) return d;
            d = DefDatabase<RoomStatDef>.GetNamedSilentFail(defName);
            roomStatCache[defName] = d;
            return d;
        }

        /// <summary>How often this kitchen poisons a meal. Falls from 5% to 0% with cleanliness.</summary>
        public static RoomStatDef FoodPoisonChanceStat { get { return RoomStat("FoodPoisonChance"); } }

        /// <summary>What this room multiplies research by. 0.75x when filthy, 1.15x when spotless.</summary>
        public static RoomStatDef ResearchSpeedFactorStat { get { return RoomStat("ResearchSpeedFactor"); } }

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

        /// <summary>
        /// A patch of ground where butchering may happen. Free and instant, like every other
        /// spot, and it is the difference between a field of corpses and food.
        /// </summary>
        public static ThingDef ButcherSpot { get { return Thing("ButcherSpot"); } }

        /// <summary>
        /// The cheapest thing to sit on: 25 of any material and no research whatsoever.
        ///
        /// Worth naming because the opposite was recorded as fact. `NeedComfort` was written up
        /// as needing Complex Furniture, which is true of an armchair and of nothing a colony
        /// actually needs — so the complaint went unanswered in every survey ever taken.
        /// </summary>
        public static ThingDef Stool { get { return Thing("Stool"); } }

        /// <summary>The smallest table. A quarter the material of the one the director asked for.</summary>
        public static ThingDef SmallTable { get { return Thing("Table1x2c"); } }
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

        /// <summary>A bed for a tamed animal. Falls back to the free spot when none can be built.</summary>
        public static ThingDef AnimalBed { get { return Thing("AnimalBed"); } }
        public static ThingDef AnimalSleepingSpot { get { return Thing("AnimalSleepingSpot"); } }

        /// <summary>
        /// A feed trough. This is a Hopper in vanilla — a storage building that a nutrient
        /// paste dispenser draws from, and the thing animals eat out of in a barn.
        /// </summary>
        public static ThingDef Hopper { get { return Thing("Hopper"); } }

        /// <summary>A flap an animal can pass without leaving the door open to the weather.</summary>
        public static ThingDef AnimalFlap { get { return Thing("AnimalFlap"); } }
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
