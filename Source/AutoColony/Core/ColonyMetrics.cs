namespace AutoColony
{
    /// <summary>
    /// The numbers a colony is scored on, with no RimWorld types attached.
    ///
    /// This exists to keep <see cref="ColonyEvaluator"/> and <see cref="EpochAccumulator"/>
    /// free of game types. RimWorld reference assemblies carry no method bodies, so anything
    /// touching a <c>Map</c> or <c>Pawn</c> can never execute in a test; isolating scoring
    /// behind a plain struct is what makes the fitness function testable at all.
    ///
    /// It also makes the evaluator's inputs explicit — the exact list of things the strategy
    /// is being judged on, in one place.
    /// </summary>
    public struct ColonyMetrics
    {
        public int day;

        // population
        public int colonists;
        public int colonistsDowned;
        public int colonistsInMentalState;
        public float avgMood;

        /// <summary>The unhappiest colonist. Breaks are an individual event, and in a colony of
        /// three a contented pair hides someone at 0.05 completely — run 58 sat at avgMood 0.48
        /// with a colonist carrying MySonDied at -20 who went berserk.</summary>
        public float minMood;

        /// <summary>The hungriest colonist's food need, 0 to 1.</summary>
        public float minFood;

        /// <summary>Colonists the game itself calls starving.</summary>
        public int colonistsStarving;

        /// <summary>Colonists who cannot path to any food — walled in.</summary>
        public int colonistsCutOff;

        /// <summary>Colonists with an untended condition the game says needs tending.</summary>
        public int colonistsUntended;

        /// <summary>Of those, how many carry something that can kill them if left.</summary>
        public int colonistsUntendedLethal;

        /// <summary>Colonists whose disease is ahead of their immunity.</summary>
        public int colonistsLosingToDisease;

        /// <summary>Days of food still inside unbutchered animal corpses.</summary>
        public float daysOfFoodUnbutchered;

        /// <summary>Days of food that will rot before it is eaten.</summary>
        public float daysOfFoodSpoiling;

        /// <summary>Days of food that is a cooked meal rather than an ingredient.</summary>
        public float daysOfMeals;

        /// <summary>Buildings whose fuel hopper the game says wants filling.</summary>
        public int buildingsWantingFuel;

        /// <summary>Units of fuel available for them. Zero with dry hoppers is a supply failure.</summary>
        public int fuelOnHand;

        /// <summary>Fuel still standing as a plant — cut it and it becomes fuel.</summary>
        public int fuelStanding;

        /// <summary>Medicine of any grade in store. Distinguishes "could not treat" from "did not".</summary>
        public int medicineCount;

        /// <summary>Of that, how much has been hauled into storage.</summary>
        public int medicineStored;

        /// <summary>
        /// Material the colony could actually put into a wall. Carried so the evaluator can see
        /// a colony that has built itself unable to build.
        /// </summary>
        public int usableMaterial;
        public float avgHealth;

        // sustenance and economy
        public float daysOfFood;

        /// <summary>
        /// Outdoor temperature in Celsius. Carried into the record rather than only the state
        /// because clothing, heating and cooling are all now driven by it, and a decision whose
        /// input never appears in the log cannot be judged from the log.
        /// </summary>
        public float outdoorTemperature;
        public float wealthTotal;
        public int colonistBeds;
        /// <summary>
        /// Turrets that can actually fire — never the raw built count.
        ///
        /// Named for what it carries. It was called `turrets` while being filled from
        /// `poweredTurrets`, which is correct behaviour under a misleading name, and the name
        /// cost a reader an incorrect "fix" to the defence score: an unpowered turret is a wall
        /// decoration, this codebase knows it, and the field looked like it had forgotten.
        /// </summary>
        public int poweredTurrets;
        public int fires;

        /// <summary>
        /// Fires close enough to the colony to be worth answering.
        ///
        /// Separate from the map-wide total on purpose. The director deliberately ignores a
        /// wildfire that will never reach the base — that is a designed behaviour, measured and
        /// kept — so scoring or diagnosing on the total punishes the colony for a decision that
        /// was correct, and describes a quiet colony as one that spent half an epoch on fire.
        /// </summary>
        public int firesNearBase;

        /// <summary>
        /// Whether the plan was answering something immediate at this moment — a fire, a raid,
        /// an empty larder.
        ///
        /// An outcome figure cannot see this. Two colonies can finish an epoch with identical
        /// mood, food and wealth while one of them spent the whole fortnight lurching from one
        /// emergency to the next; that one is not being run as well, and it is the one about to
        /// come apart.
        /// </summary>
        public bool inEmergency;

        /// <summary>
        /// How many standing rooms the game could rate at this moment, and how many of those
        /// met the floor their role asks for.
        ///
        /// Set alongside <see cref="inEmergency"/> from the director loop rather than derived in
        /// <c>ToMetrics</c>, because it needs the layout and the state does not carry one.
        /// </summary>
        public int roomsJudged;
        public int roomsUpToStandard;

        // cumulative counters, differenced across an epoch
        public int researchFinished;
        public int cumulativeDeaths;
        public int cumulativeRaids;

        /// <summary>A colony with nobody in it cannot be scored meaningfully.</summary>
        public bool Valid { get { return colonists > 0; } }

        /// <summary>Neutral metrics for a healthy one-colonist colony. Test and fallback default.</summary>
        public static ColonyMetrics Neutral()
        {
            var m = new ColonyMetrics();
            m.colonists = 1;
            m.avgMood = 0.5f;
            m.avgHealth = 1f;
            m.daysOfFood = 10f;
            m.wealthTotal = 1000f;
            m.colonistBeds = 1;
            return m;
        }
    }
}
