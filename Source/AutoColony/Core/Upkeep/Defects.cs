using System.Collections.Generic;

namespace AutoColony.Upkeep
{
    /// <summary>
    /// Something the colony has built that is wrong — as opposed to something it lacks.
    ///
    /// Goals cover the second case: no power, no freezer, nowhere to sleep, so build one.
    /// Nothing covered the first. Every construction path placed blueprints into rooms the
    /// planner had reserved, and the single repair path fired only when a room's key furniture
    /// was *absent*. A stove standing outdoors in the rain is not absent, so nothing looked at
    /// it; a bedroom with no lamp is not missing a bed, so nothing looked at that either. The
    /// colony could be visibly, measurably wrong and still count as finished.
    /// </summary>
    public enum DefectKind
    {
        /// <summary>An electrical building under open sky. Rain shorts it out and starts a fire.</summary>
        ExposedPowered,

        /// <summary>A room colonists live in with no light in it.</summary>
        DarkRoom,

        /// <summary>
        /// Several colonists sharing one room, *while the colony can afford to separate them*.
        /// Sharing is not a fault in itself — see <see cref="BuildingMeans"/>.
        /// </summary>
        SharedBedroom,

        /// <summary>A bedroom bad enough that sleeping in it costs mood.</summary>
        DrearyRoom,

        /// <summary>
        /// More building than the colony can now afford to keep. The walls hold material it
        /// needs back, so the surplus comes down and everyone moves in together.
        /// </summary>
        Overbuilt,

        /// <summary>
        /// Somebody is lying where they fell. The largest single mood penalty in the game, and
        /// a grave costs nothing at all — which is why it outranks everything else here.
        /// </summary>
        UnburiedDead,

        /// <summary>Colonists are cold. A heater needs electricity; a campfire needs neither
        /// that nor research, so this is always answerable.</summary>
        ColdRoom,

        /// <summary>
        /// Colonists are too hot. Unlike cold this has no low-technology answer — a cooler needs
        /// AirConditioning and a working grid — so it is often reported rather than fixed. It was
        /// previously mapped to nothing at all, which made the largest complaint in one survey
        /// invisible to everything that might have acted on it.
        /// </summary>
        HotRoom,

        /// <summary>Nowhere to eat off a table, which every colonist pays for at every meal.</summary>
        NoTable,

        /// <summary>Nothing to do. Recreation is cheap and the penalty compounds.</summary>
        Cheerless,

        /// <summary>
        /// Nowhere to sit. Paid standing at a table, standing at a bench, and standing anywhere
        /// else the colonist spends their day.
        ///
        /// Recorded here as unfixable for the whole life of this project, on the belief that
        /// seating needed Complex Furniture. That is true of an armchair. A stool is twenty-five
        /// units of anything and no research at all, and the complaint has been in the "cannot
        /// fix yet" column of essentially every survey ever taken.
        /// </summary>
        Uncomfortable
    }

    /// <summary>
    /// Something the colony is unhappy about that the director has no answer for.
    ///
    /// Carried as a value rather than a formatted line. The survey used to hand the module
    /// strings like "EnvironmentCold (-4.0)" and the module parsed the number back out to feed
    /// the scorer — an undocumented format contract spanning two files, and a sort that ordered
    /// alphabetically when the consumer wanted the worst first.
    /// </summary>
    public struct UnmetComplaint
    {
        public string thought;
        public float mood;      // magnitude, always positive

        public UnmetComplaint(string thought, float mood)
        {
            this.thought = thought;
            this.mood = mood < 0f ? -mood : mood;
        }

        public override string ToString()
        {
            return thought + " (-" + mood.ToString("0.0") + ")";
        }
    }

    /// <summary>What to actually do about a defect.</summary>
    public enum RemedyKind
    {
        None,

        /// <summary>Ask for a roof over it, which is far cheaper than moving it.</summary>
        RoofOver,

        /// <summary>Tear it down, so the planner re-places it somewhere it belongs.</summary>
        Relocate,

        /// <summary>Put a lamp in the room.</summary>
        AddLight,

        /// <summary>Cool the room. Needs AirConditioning and a working grid.</summary>
        AddCooler,

        /// <summary>Take out the beds that make a bedroom a barracks.</summary>
        RemoveSurplusBeds,

        /// <summary>Something in the room worth looking at.</summary>
        AddBeauty,

        /// <summary>
        /// Take a surplus room down and get the material in its walls back. The opposite move to
        /// <see cref="RemoveSurplusBeds"/>, and the right one when the colony has fallen on hard
        /// times since it built out.
        /// </summary>
        Reclaim,

        /// <summary>Dig a grave, so the dead stop costing the living.</summary>
        BuryDead,

        /// <summary>Put a heater in.</summary>
        AddHeater,

        /// <summary>A table to eat at.</summary>
        AddTable,

        /// <summary>Something to do that is not work.</summary>
        AddRecreation,

        /// <summary>Somewhere to sit. A stool needs no research and almost no material.</summary>
        AddSeating
    }

    /// <summary>
    /// What the colonists are actually complaining about, keyed by the game's own thought.
    ///
    /// The director previously read mood only as an average, which says a colony is unhappy but
    /// never why — and "why" is the only part that can be acted on. Reading the thoughts means
    /// the colony responds to its measured experience rather than to a rule someone guessed.
    ///
    /// Adding a case is adding a row here plus a remedy. Complaints with no row are still
    /// counted and reported, so what the colony suffers from and cannot yet fix stays visible
    /// instead of silently dropping out.
    /// </summary>
    public static class Complaints
    {
        static readonly Dictionary<string, DefectKind> byThought = new Dictionary<string, DefectKind>
        {
            // "I've been in the dark for a while." -5, and it follows them wherever they are.
            { "EnvironmentDark", DefectKind.DarkRoom },

            // Sharing is punished far harder than a small private room: an awful barracks is -7
            // against an awful bedroom's -2, so splitting one is worth more than decorating it.
            { "SleptInBarracks", DefectKind.SharedBedroom },

            // Only its lower stages are negative, so the survey reads the actual mood offset
            // rather than assuming the thought is bad news.
            { "SleptInBedroom", DefectKind.DrearyRoom },

            // -10 and entirely self-inflicted: a grave needs no research and costs nothing, and
            // a colony carried this one for eleven days before it died of the accumulation.
            { "ColonistLeftUnburied", DefectKind.UnburiedDead },
            { "ObservedLayingCorpse", DefectKind.UnburiedDead },

            { "EnvironmentCold", DefectKind.ColdRoom },
            { "SleptInCold", DefectKind.ColdRoom },
            { "EnvironmentHot", DefectKind.HotRoom },
            { "SleptInHeat", DefectKind.HotRoom },

            // Paid by every colonist at every meal, and the table was only ever placed inside a
            // Dining room — a discretionary pick a struggling colony never reaches.
            { "AteWithoutTable", DefectKind.NoTable },

            { "NeedJoy", DefectKind.Cheerless },
            { "NeedBeauty", DefectKind.DrearyRoom },

            // A stool answers this and nothing ever offered one.
            { "NeedComfort", DefectKind.Uncomfortable }
        };

        /// <summary>The defect a complaint points at, when it is one the director can act on.</summary>
        public static bool TryMap(string thoughtDefName, out DefectKind kind)
        {
            kind = DefectKind.DarkRoom;
            if (string.IsNullOrEmpty(thoughtDefName)) return false;
            return byThought.TryGetValue(thoughtDefName, out kind);
        }


        /// <summary>Below this a complaint is background noise, not worth reporting.</summary>
        public const float ReportableSeverity = 0.2f;

        /// <summary>How much a mood penalty of this size matters, 0 to 1.</summary>
        public static float Severity(float moodOffset)
        {
            if (moodOffset >= 0f) return 0f;
            float magnitude = -moodOffset / 10f;   // -10 is about as bad as one thought gets
            return magnitude > 1f ? 1f : magnitude;
        }
    }

    /// <summary>
    /// Which defect to spend a pass on, and what to do about it.
    ///
    /// Free of every game type so the judgement can be tested offline — what deserves attention
    /// is the part most likely to be wrong, not the placement call underneath it.
    /// </summary>
    public static class DefectPolicy
    {
        /// <summary>Below this, a defect is not worth spending colonist time on.</summary>
        public const float ActionThreshold = 0.15f;

        /// <summary>
        /// The default remedy for a kind. Exposed equipment is the exception: roofing is far
        /// cheaper than moving, so the survey chooses between the two on whether the cell can
        /// actually hold a roof, and only tears the building down when it cannot.
        /// </summary>
        public static RemedyKind RemedyFor(DefectKind kind)
        {
            switch (kind)
            {
                case DefectKind.ExposedPowered: return RemedyKind.RoofOver;
                case DefectKind.DarkRoom: return RemedyKind.AddLight;
                case DefectKind.SharedBedroom: return RemedyKind.RemoveSurplusBeds;
                case DefectKind.DrearyRoom: return RemedyKind.AddBeauty;
                case DefectKind.Overbuilt: return RemedyKind.Reclaim;
                case DefectKind.UnburiedDead: return RemedyKind.BuryDead;
                case DefectKind.ColdRoom: return RemedyKind.AddHeater;
                case DefectKind.HotRoom: return RemedyKind.AddCooler;
                case DefectKind.NoTable: return RemedyKind.AddTable;
                case DefectKind.Uncomfortable: return RemedyKind.AddSeating;
                case DefectKind.Cheerless: return RemedyKind.AddRecreation;
                default: return RemedyKind.None;
            }
        }

        /// <summary>
        /// How much attention a defect deserves. Severity is what it costs the colony; the
        /// weight is how well fixing it pays for itself.
        ///
        /// Reclaiming outranks everything, because it only ever fires when the colony is short
        /// of the material every other remedy is about to spend. Exposed equipment is next: its
        /// downside is a fire rather than a frown, and the fire takes the building with it. Then
        /// a dark room, where one cheap lamp settles the complaint outright. Beauty is last —
        /// the most work for the smallest movement.
        /// </summary>
        public static float Priority(DefectKind kind, float severity)
        {
            return Priority(kind, severity, 1f);
        }

        /// <summary>
        /// As above, scaled by how much the room in question matters.
        ///
        /// Ranking on the fault alone treated every place as interchangeable — a dark room scored
        /// the same whether it was the only kitchen or a spare bedroom nobody sleeps in. The room
        /// term is what stops a colony lighting an empty store while the room everyone eats in is
        /// the actual problem.
        /// </summary>
        public static float Priority(DefectKind kind, float severity, float roomImportance)
        {
            return Priority(kind, severity, roomImportance, Weight(kind));
        }

        /// <summary>
        /// As above with the kind's weight supplied rather than looked up, so the caller can
        /// hand in the colony's own learned value instead of a constant compiled in here.
        /// </summary>
        public static float Priority(DefectKind kind, float severity, float roomImportance,
                                     float kindWeight)
        {
            if (severity <= 0f) return 0f;
            if (roomImportance <= 0f) roomImportance = 1f;
            if (kindWeight < 0f) kindWeight = 0f;
            return severity * kindWeight * roomImportance;
        }

        /// <summary>
        /// How well fixing each kind of fault pays for itself.
        ///
        /// These were a hardcoded table — nine numbers somebody reasoned their way to once,
        /// fixed for every colony in every situation. They are the director's whole opinion
        /// about what to do next, and the search was not allowed to have one.
        ///
        /// They are now supplied from outside, which means from the genome, so a colony can
        /// learn that its own circumstances make burying the dead or roofing the generator the
        /// thing that pays. The values below are the starting point rather than the answer.
        /// </summary>
        public static readonly float[] DefaultWeights = BuildDefaults();

        static float[] BuildDefaults()
        {
            // Sized from the enum directly, not from KindCount. Static fields initialise in
            // declaration order, so KindCount is still zero when this runs — which produced an
            // empty weight table and a type initialiser that threw on first touch.
            var weights = new float[System.Enum.GetValues(typeof(DefectKind)).Length];
            for (int i = 0; i < weights.Length; i++) weights[i] = 0.5f;

            // A free building that removes the largest penalty in the game. Nothing else here
            // has that ratio, which is why it starts highest — not why it must stay there.
            weights[(int)DefectKind.UnburiedDead] = 2.0f;
            weights[(int)DefectKind.Overbuilt] = 1.6f;
            weights[(int)DefectKind.ExposedPowered] = 1.4f;
            weights[(int)DefectKind.ColdRoom] = 1.3f;      // cold kills, unlike the rest of these
            weights[(int)DefectKind.HotRoom] = 1.3f;
            weights[(int)DefectKind.DarkRoom] = 1.2f;
            weights[(int)DefectKind.SharedBedroom] = 1.0f;
            weights[(int)DefectKind.NoTable] = 0.9f;       // small, but every colonist every meal
            weights[(int)DefectKind.Cheerless] = 0.7f;
            weights[(int)DefectKind.Uncomfortable] = 0.7f; // small each time, paid all day long
            weights[(int)DefectKind.DrearyRoom] = 0.6f;
            return weights;
        }

        /// <summary>Number of defect kinds, so a weight vector can be sized without a lookup.</summary>
        public static readonly int KindCount = System.Enum.GetValues(typeof(DefectKind)).Length;

        /// <summary>The name a weight gene carries for a given kind.</summary>
        public static string WeightKey(DefectKind kind)
        {
            return "upkeep.w." + kind;
        }

        static float Weight(DefectKind kind)
        {
            int i = (int)kind;
            return i >= 0 && i < DefaultWeights.Length ? DefaultWeights[i] : 0.5f;
        }

        /// <summary>Whether this is worth acting on at all.</summary>
        public static bool WorthActing(DefectKind kind, float severity)
        {
            return RemedyFor(kind) != RemedyKind.None && Priority(kind, severity) >= ActionThreshold;
        }
    }
}
