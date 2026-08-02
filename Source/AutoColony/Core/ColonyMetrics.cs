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
        public int turrets;
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
