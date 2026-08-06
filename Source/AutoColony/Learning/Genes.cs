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
        public const string ResearchUrgentWeight = "research.urgentWeight";
        public const string ResearchPressureBias = "research.pressureBias";
        public const string BillBacklogWeight = "work.billBacklog";
        public const string FodderCellsPerAnimal = "fodder.cellsPerAnimal";
        public const string GrowthFoodMargin = "growth.foodMargin";
        public const string TeaMoodFloor = "comfort.teaMoodFloor";
        public const string ComponentsTarget = "components.target";
        public const string TextilesTarget = "textiles.target";

        // ---- production -------------------------------------------------------------
        public const string MealsPerColonist = "meals.perColonist";
        public const string ProductionBuffer = "production.bufferFactor";

        /// <summary>
        /// How much a room being depended on, and how much a room being busy, raise the priority
        /// of fixing something wrong with it.
        ///
        /// Genes rather than constants because the trade-off is genuinely strategic: favouring
        /// the essential room suits a colony one failure from collapse, favouring the busy one
        /// suits a large settled colony where mood is the binding constraint. There is no single
        /// right answer, which is what makes it a question for the search.
        /// </summary>
        public const string RoomEssentialWeight = "room.essentialWeight";
        public const string RoomOccupancyWeight = "room.occupancyWeight";

        // ---- work assignment shape --------------------------------------------------
        public const string WorkSkillWeight = "work.skillWeight";
        public const string WorkPassionWeight = "work.passionWeight";

        /// <summary>
        /// How much longer a wait really takes than the arithmetic says, before the colony has
        /// met that kind of wait often enough to have learned its own answer.
        ///
        /// The estimate assumes the measured rate holds and it does not — researchers get
        /// pulled onto hauling, builders get drafted. This is the prior; PatienceMemory
        /// replaces it with evidence the moment there is any.
        /// </summary>
        public const string PlannerPatienceSlack = "planner.patienceSlack";

        /// <summary>
        /// How long a goal is stood down for, as a fraction of the wait it just failed to make
        /// good on. A goal that held the plan six days and moved nothing should not be back in
        /// an hour.
        /// </summary>
        public const string PlannerDemotionFraction = "planner.demotionFraction";

        /// <summary>
        /// The longest any goal may hold the plan, in days, however patient the arithmetic says
        /// to be. There is no honest derivation for this one — it is the point past which
        /// holding on stops being patience and becomes a colony doing one thing while
        /// everything else rots — so it is a gene and says so.
        /// </summary>
        public const string PlannerPatienceCeiling = "planner.patienceCeiling";
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

        /// <summary>
        /// How favourable a fight has to look before the colony meets it in the open, as a ratio
        /// of colony strength to threat.
        ///
        /// Deliberately a gene rather than a constant. There is no right answer to write down —
        /// a colony with cover and rifles should hold ground a colony of three wounded farmers
        /// should not — and it is exactly the kind of judgement the search can learn from
        /// whether colonies that made it lived. Zero means never withdraw, which is what the
        /// director did before it could.
        /// </summary>
        public const string DefenseEngageRatio = "defense.engageRatio";
        public const string DefenseTurretCount = "defense.turretCount";
        public const string DefenseFiresPerColonist = "defense.firesPerColonist";
        public const string HuntWoundsPerHealth = "hunt.woundsPerHealth";

        /// <summary>
        /// Where colonists stand to fight, as a set of competing preferences rather than a rule.
        ///
        /// The director had no concept of position at all: everyone was sent to the base origin
        /// and told to shoot whatever was nearest. How much cover is worth against how much
        /// spacing has no fixed answer — it depends on whether the enemy throws grenades,
        /// whether the colony has rifles or clubs, and how much of the base is walled — so these
        /// are questions for the search rather than constants to assert.
        /// </summary>
        public const string CombatCoverWeight = "combat.coverWeight";
        public const string CombatStandoffWeight = "combat.standoffWeight";
        public const string CombatPreferredRange = "combat.preferredRange";
        public const string CombatSpreadWeight = "combat.spreadWeight";
        public const string CombatChokepointWeight = "combat.chokepointWeight";
        public const string CombatIndoorsWeight = "combat.indoorsWeight";
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
            Add(ResearchUrgentWeight, 1.5f, 4f, 2.6f, "Work", "Research work weight while the plan is blocked on a project");
            Add(ResearchPressureBias, 0f, 2f, 0.6f, "Research", "How much blocked goals steer project choice");
            Add(BillBacklogWeight, 0f, 3f, 1.2f, "Work", "How hard waiting production bills pull their work type");
            Add(FodderCellsPerAnimal, 6f, 20f, 12f, "Farm", "Hay cells sown per animal");
            Add(GrowthFoodMargin, 4f, 10f, 6f, "Growth", "Days of food before another mouth is wanted");
            Add(TeaMoodFloor, 0.25f, 0.45f, 0.35f, "Comfort", "Mood below which tea is allowed for joy");
            Add(ComponentsTarget, 0f, 50f, 8f, "Stock targets", "Components stock target");
            Add(TextilesTarget, 0f, 500f, 100f, "Stock targets", "Cloth/leather stock target");

            Add(MealsPerColonist, 1f, 15f, 5f, "Production", "Cooked meals per colonist");
            Add(ProductionBuffer, 1f, 3f, 1.5f, "Production", "Overshoot factor on bill targets");
            Add(RoomEssentialWeight, 0f, 2f, 0.8f, "Rooms", "Weight on a room the colony depends on");
            Add(RoomOccupancyWeight, 0f, 2f, 0.6f, "Rooms", "Weight on how busy a room is");

            Add(WorkSkillWeight, 0f, 3f, 1f, "Work", "Weight on skill level");
            Add(WorkPassionWeight, 0f, 3f, 1f, "Work", "Weight on passion");
            Add(WorkNeedWeight, 0f, 3f, 1.2f, "Work", "Weight on current colony need");
            Add(WorkSpread, 0f, 1f, 0.5f, "Work", "How widely each colonist is assigned");
            Add(WorkBands, 1f, 4f, 3f, "Work", "Distinct priority bands used");

            // Defaults reproduce the old flat behaviour wherever no estimate can be made:
            // ~0.5 days of patience and ~1 day of demotion, which is what FocusGraceTicks and
            // DemotionTicks were. The change is a strict improvement where it bites and
            // neutral everywhere else.
            Add(PlannerPatienceSlack, 1f, 3f, 1.5f, "Planner", "Slack on an estimated wait");
            Add(PlannerDemotionFraction, 0.25f, 2f, 1f, "Planner", "Stand-down as a fraction of the wait");
            Add(PlannerPatienceCeiling, 2f, 20f, 6f, "Planner", "Longest a goal may hold the plan, days");

            Add(BaseRoomSize, 4f, 11f, 7f, "Base", "Planned room interior size");
            Add(BaseSpareBeds, 0f, 5f, 1f, "Base", "Spare beds kept ready");
            Add(BaseStonePreference, 0f, 1f, 0.4f, "Base", "Stone vs wood for walls");
            // Private rooms lift mood but cost far more to build: a real strategic trade-off.
            // One, not two.
            //
            // Two beds in a room is not a bedroom with a guest, it is a Barracks to the game —
            // watched live in run 53, on the director's own bedroom, the moment its second bed
            // was built. The curves are not close: SleptInBedroom pays -2 up to +8 and
            // SleptInBarracks -7 up to +4, so sharing is worse at the floor and lower at the
            // ceiling, in every band there is.
            //
            // This is only what a *comfortable* colony prefers. BuildingMeans.BedsPerRoom
            // already puts everyone in one room when the colony is destitute and scales between
            // the two, so the poverty case was always handled elsewhere and this value was
            // quietly costing five mood a head a night to save walls the colony could afford.
            // Still a gene: evolution may raise it if it finds a reason.
            Add(BaseBedsPerRoom, 1f, 4f, 1f, "Base", "Beds per bedroom");

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
            Add(DefenseEngageRatio, 0f, 1.5f, 0.35f, "Defense", "Strength ratio needed to fight in the open");
            Add(DefenseTurretCount, 0f, 12f, 3f, "Defense", "Target turret count");
            Add(DefenseFiresPerColonist, 1f, 20f, 6f, "Defense",
                "Fires one free colonist can beat before the front outruns them");
            Add(HuntWoundsPerHealth, 1f, 12f, 4f, "Defense",
                "Wounding hits a hunt takes per unit of the animal's health scale");
            Add(CombatCoverWeight, 0f, 8f, 4f, "Combat positioning", "Value of cover");
            Add(CombatStandoffWeight, 0f, 2f, 0.3f, "Combat positioning", "Value of holding a range");
            Add(CombatPreferredRange, 2f, 30f, 12f, "Combat positioning", "Range to fight at");
            Add(CombatSpreadWeight, 0f, 6f, 2f, "Combat positioning", "Value of spacing out");
            Add(CombatChokepointWeight, 0f, 6f, 1.5f, "Combat positioning", "Value of a chokepoint");
            Add(CombatIndoorsWeight, 0f, 6f, 1f, "Combat positioning", "Value of fighting indoors");
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

        /// <summary>
        /// Registers the priority weight gene covering a work type discovered at startup.
        ///
        /// One gene per *category*, not per work type. <see cref="Register"/> ignores a key it
        /// already holds, so twenty work types produce six genes.
        /// </summary>
        /// <summary>
        /// Registers a gene for every kind of fault the upkeep layer can find.
        ///
        /// These were a hardcoded table: numbers reasoned to once and fixed for every colony
        /// forever. They are the director's entire opinion about what to do next, and that is
        /// exactly the sort of opinion worth earning from outcomes rather than asserting — a
        /// colony under constant raids and one building out in peace do not agree about whether
        /// roofing the generator beats lighting the bedroom.
        /// </summary>
        /// <summary>
        /// Registers one weight for one aspect of where a kind of furniture wants to stand.
        ///
        /// Four aspects each — clearance from the door, open sides to work from, a wall at the
        /// back, and distance from other furniture — because a bed and a workbench want
        /// genuinely opposite things and a single ordering served neither.
        /// </summary>
        /// <summary>
        /// Registers a role's siting preferences and its dimensions.
        ///
        /// Where a room goes was a fixed pattern that gave every role the same answer to a
        /// question that differs completely between them, and its size was one number shared by
        /// all of them. A store wants to be central and large; a bedroom wants to be small and
        /// close; a prison wants to be far away.
        /// </summary>
        public static void RegisterSiting(string role, float compactness, float evenness,
                                          float partner, float resource, int width, int height)
        {
            if (string.IsNullOrEmpty(role)) return;
            Register(new GeneSpec("site." + role + ".compactness", 0f, 3f, compactness,
                                  "Room siting", role + " closeness to base"));
            Register(new GeneSpec("site." + role + ".evenness", 0f, 3f, evenness,
                                  "Room siting", role + " even spacing"));
            Register(new GeneSpec("site." + role + ".partner", 0f, 3f, partner,
                                  "Room siting", role + " closeness to partner room"));
            Register(new GeneSpec("site." + role + ".resource", 0f, 3f, resource,
                                  "Room siting", role + " closeness to resource"));
            Register(new GeneSpec("site." + role + ".width", 5f, 13f, width,
                                  "Room siting", role + " width"));
            Register(new GeneSpec("site." + role + ".height", 5f, 13f, height,
                                  "Room siting", role + " height"));
        }

        public static void RegisterPlacementWeight(string kind, string aspect, float def)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(aspect)) return;
            Register(new GeneSpec("furniture." + kind + "." + aspect, 0f, 4f, def,
                                  "Furniture placement", kind + " " + aspect));
        }

        public static void RegisterUpkeepWeights(IList<string> keys, IList<float> defaults)
        {
            if (keys == null || defaults == null) return;
            for (int i = 0; i < keys.Count && i < defaults.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) continue;
                Register(new GeneSpec(keys[i], 0f, 3f, defaults[i], "Upkeep weights",
                                      "Upkeep: " + keys[i].Replace("upkeep.w.", "")));
            }
        }

        public static void RegisterWorkType(string workDefName, string label)
        {
            string category = CategoryOf(workDefName);
            Register(new GeneSpec(WorkWeightPrefix + category, 0f, 3f, 1f,
                                  "Work weights", "Work: " + category));
        }

        public static string WorkKey(string workDefName)
        {
            return WorkWeightPrefix + CategoryOf(workDefName);
        }

        /// <summary>
        /// Which weight a work type shares.
        ///
        /// The search had one gene per work type — around twenty of a fifty-eight gene genome —
        /// against a handful of epochs, and a colony's score is far noisier than the difference
        /// those genes make. Measured directly this session: two candidates replayed from an
        /// identical healthy world scored 0.561 and 0.565, a spread far below the ~0.02 at which
        /// the search is already known to go flat. Expressiveness the evidence cannot support is
        /// not expressiveness, it is dilution — every extra dimension spends the same limited
        /// number of trials.
        ///
        /// Grouped by what the work is *for*, since that is the level a strategy actually varies
        /// at: a colony leans towards feeding itself or towards building, not towards hauling
        /// over cleaning. Work types from other mods fall to "other" rather than each adding a
        /// gene, which keeps them covered without paying for them.
        /// </summary>
        public static string CategoryOf(string workDefName)
        {
            switch (workDefName)
            {
                case "Firefighter":
                case "Patient":
                case "PatientBedRest":
                case "Doctor":
                    return "health";

                case "Growing":
                case "Hunting":
                case "Cooking":
                    return "food";

                case "Construction":
                case "Mining":
                case "PlantCutting":
                case "Smoothing":
                    return "build";

                case "Smithing":
                case "Tailoring":
                case "Crafting":
                case "Art":
                    return "craft";

                case "Hauling":
                case "Cleaning":
                    return "logistics";

                case "Research":
                case "Warden":
                case "Handling":
                    return "colony";

                default:
                    return "other";
            }
        }

        public static GeneSpec Spec(string key)
        {
            GeneSpec s;
            return specMap.TryGetValue(key, out s) ? s : null;
        }
    }
}
