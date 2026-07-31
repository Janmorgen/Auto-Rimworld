using System.Collections.Generic;

namespace AutoColony.Learning
{
    /// <summary>
    /// Metadata for one tunable parameter of the colony-management strategy.
    /// Bounds matter: the optimiser mutates in units of the gene's range, so a
    /// gene's Min/Max define both its legal values and its mutation scale.
    /// </summary>
    public class GeneSpec
    {
        public readonly string Key;
        public readonly float Min;
        public readonly float Max;
        public readonly float Default;
        public readonly string Group;
        public readonly string Label;

        public GeneSpec(string key, float min, float max, float def, string group, string label)
        {
            Key = key;
            Min = min;
            Max = max;
            Default = def;
            Group = group;
            Label = label;
        }

        public float Range { get { return Max - Min; } }

        public float Clamp(float v)
        {
            if (v < Min) return Min;
            if (v > Max) return Max;
            return v;
        }
    }

    /// <summary>
    /// The strategy search space. Every knob the director uses to make a decision
    /// lives here, so "learning a management strategy" reduces to searching this vector.
    ///
    /// Work-type weights are registered at runtime from the def database rather than
    /// hardcoded, so the genome automatically covers work types added by other mods.
    /// </summary>
    public static class Genes
    {
        // ---- resource stock targets -------------------------------------------------
        public const string FoodDaysPerColonist = "food.daysPerColonist";
        public const string MedicinePerColonist = "medicine.perColonist";
        public const string WoodTarget = "wood.target";
        public const string SteelTarget = "steel.target";
        public const string ComponentsTarget = "components.target";
        public const string TextilesTarget = "textiles.target";

        // ---- production -------------------------------------------------------------
        public const string MealsPerColonist = "meals.perColonist";
        public const string ProductionBuffer = "production.bufferFactor";

        // ---- work assignment shape --------------------------------------------------
        public const string WorkSkillWeight = "work.skillWeight";
        public const string WorkPassionWeight = "work.passionWeight";
        public const string WorkNeedWeight = "work.needWeight";
        public const string WorkSpread = "work.spread";
        public const string WorkBands = "work.bands";

        // ---- base planning ----------------------------------------------------------
        public const string BaseRoomSize = "base.roomSize";
        public const string BaseSpareBeds = "base.spareBeds";
        public const string BaseStonePreference = "base.stonePreference";
        public const string BaseBedsPerRoom = "base.bedsPerRoom";

        // ---- zones ------------------------------------------------------------------
        public const string GrowingCellsPerColonist = "growing.cellsPerColonist";
        public const string StockpileCellsPerColonist = "stockpile.cellsPerColonist";

        // ---- raw resource gathering -------------------------------------------------
        public const string MiningAggression = "mining.aggression";
        public const string ChopAggression = "chop.aggression";
        public const string HuntAggression = "hunt.aggression";

        // ---- research ---------------------------------------------------------------
        public const string ResearchCheapBias = "research.cheapBias";
        public const string ResearchUnlockBias = "research.unlockBias";
        public const string ResearchExplore = "research.explore";

        // ---- defense ----------------------------------------------------------------
        public const string DefenseWealthRatio = "defense.wealthRatio";
        public const string DefenseDraftDanger = "defense.draftDanger";
        public const string DefenseRetreatHealth = "defense.retreatHealth";
        public const string DefenseTurretCount = "defense.turretCount";
        public const string FireResponseRadius = "defense.fireRadius";
        public const string FireRiskAversion = "defense.fireAversion";

        // ---- colonist policy --------------------------------------------------------
        public const string ColonistRecruitBias = "colonist.recruitBias";
        public const string ColonistMedCare = "colonist.medCare";
        public const string ColonistSelfTend = "colonist.selfTend";

        // ---- item claiming ----------------------------------------------------------
        public const string ItemClaimRadius = "items.claimRadius";
        public const string ItemClaimDuringDanger = "items.claimDuringDanger";

        // ---- incident policy --------------------------------------------------------
        public const string IncidentRiskTolerance = "incident.riskTolerance";

        /// <summary>Prefix for the per-work-type priority weights registered at startup.</summary>
        public const string WorkWeightPrefix = "work.w.";

        static readonly List<GeneSpec> specList = new List<GeneSpec>();
        static readonly Dictionary<string, GeneSpec> specMap = new Dictionary<string, GeneSpec>();

        public static List<GeneSpec> All { get { return specList; } }

        static Genes()
        {
            Add(FoodDaysPerColonist, 2f, 30f, 8f, "Stock targets", "Days of food per colonist");
            Add(MedicinePerColonist, 0f, 10f, 2f, "Stock targets", "Medicine per colonist");
            Add(WoodTarget, 0f, 2000f, 400f, "Stock targets", "Wood stock target");
            Add(SteelTarget, 0f, 2000f, 300f, "Stock targets", "Steel stock target");
            Add(ComponentsTarget, 0f, 50f, 8f, "Stock targets", "Components stock target");
            Add(TextilesTarget, 0f, 500f, 100f, "Stock targets", "Cloth/leather stock target");

            Add(MealsPerColonist, 1f, 15f, 5f, "Production", "Cooked meals per colonist");
            Add(ProductionBuffer, 1f, 3f, 1.5f, "Production", "Overshoot factor on bill targets");

            Add(WorkSkillWeight, 0f, 3f, 1f, "Work", "Weight on skill level");
            Add(WorkPassionWeight, 0f, 3f, 1f, "Work", "Weight on passion");
            Add(WorkNeedWeight, 0f, 3f, 1.2f, "Work", "Weight on current colony need");
            Add(WorkSpread, 0f, 1f, 0.5f, "Work", "How widely each colonist is assigned");
            Add(WorkBands, 1f, 4f, 3f, "Work", "Distinct priority bands used");

            Add(BaseRoomSize, 4f, 11f, 7f, "Base", "Planned room interior size");
            Add(BaseSpareBeds, 0f, 5f, 1f, "Base", "Spare beds kept ready");
            Add(BaseStonePreference, 0f, 1f, 0.4f, "Base", "Stone vs wood for walls");
            // Private rooms lift mood but cost far more to build: a real strategic trade-off.
            Add(BaseBedsPerRoom, 1f, 4f, 2f, "Base", "Beds per bedroom");

            Add(GrowingCellsPerColonist, 10f, 200f, 60f, "Zones", "Growing cells per colonist");
            Add(StockpileCellsPerColonist, 10f, 120f, 40f, "Zones", "Stockpile cells per colonist");

            Add(MiningAggression, 0f, 1f, 0.5f, "Gathering", "Mining aggression");
            Add(ChopAggression, 0f, 1f, 0.5f, "Gathering", "Tree felling aggression");
            Add(HuntAggression, 0f, 1f, 0.5f, "Gathering", "Hunting aggression");

            Add(ResearchCheapBias, 0f, 2f, 1f, "Research", "Bias toward cheap projects");
            // Cheapness alone opens with whatever costs least, which in vanilla is a cosmetic
            // dead end. Favouring projects that unlock others pushes toward foundational tech.
            Add(ResearchUnlockBias, 0f, 2f, 1.2f, "Research", "Bias toward tech that unlocks more");
            Add(ResearchExplore, 0.1f, 2f, 0.7f, "Research", "Bandit exploration constant");

            Add(DefenseWealthRatio, 0f, 0.2f, 0.05f, "Defense", "Defense spend vs wealth");
            Add(DefenseDraftDanger, 0f, 2f, 1f, "Defense", "Danger level that triggers drafting");
            Add(DefenseRetreatHealth, 0.1f, 0.9f, 0.45f, "Defense", "Health fraction to retreat at");
            Add(DefenseTurretCount, 0f, 12f, 3f, "Defense", "Target turret count");
            // How far out a fire is still worth walking to. Too small and a fire creeps in;
            // too large and colonists cross the map for a blaze that was never coming.
            Add(FireResponseRadius, 10f, 120f, 45f, "Defense", "Range fires are fought within");
            // How hard fire risk pushes construction toward stone. Building everything in
            // stone is slow and expensive; building everything in wood eventually burns.
            Add(FireRiskAversion, 0f, 2f, 1f, "Defense", "How much fire risk favours stone");

            Add(ColonistRecruitBias, 0f, 1f, 0.6f, "Colonists", "Willingness to recruit prisoners");
            Add(ColonistMedCare, 0f, 4f, 3f, "Colonists", "Default medical care level");
            Add(ColonistSelfTend, 0f, 1f, 1f, "Colonists", "Allow self-tending");

            // How far the colony will walk to claim loot is a genuine trade-off: distant items
            // are free material, but the trip costs work time and exposes the hauler.
            Add(ItemClaimRadius, 10f, 150f, 60f, "Items", "Range items are claimed within");
            Add(ItemClaimDuringDanger, 0f, 1f, 0.2f, "Items", "Willingness to haul during a threat");

            Add(IncidentRiskTolerance, 0f, 1f, 0.5f, "Incidents", "Appetite for risky offers");
        }

        static void Add(string key, float min, float max, float def, string group, string label)
        {
            Register(new GeneSpec(key, min, max, def, group, label));
        }

        public static void Register(GeneSpec spec)
        {
            if (specMap.ContainsKey(spec.Key)) return;
            specMap[spec.Key] = spec;
            specList.Add(spec);
        }

        /// <summary>Registers a priority weight gene for a work type discovered at startup.</summary>
        public static void RegisterWorkType(string workDefName, string label)
        {
            Register(new GeneSpec(WorkWeightPrefix + workDefName, 0f, 3f, 1f, "Work weights", label));
        }

        public static string WorkKey(string workDefName)
        {
            return WorkWeightPrefix + workDefName;
        }

        public static GeneSpec Spec(string key)
        {
            GeneSpec s;
            return specMap.TryGetValue(key, out s) ? s : null;
        }
    }
}
