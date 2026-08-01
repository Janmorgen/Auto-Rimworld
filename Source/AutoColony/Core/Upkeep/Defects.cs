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

        /// <summary>Colonists are cold. A heater needs only electricity.</summary>
        ColdRoom,

        /// <summary>Nowhere to eat off a table, which every colonist pays for at every meal.</summary>
        NoTable,

        /// <summary>Nothing to do. Recreation is cheap and the penalty compounds.</summary>
        Cheerless
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
        AddRecreation
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

            // Paid by every colonist at every meal, and the table was only ever placed inside a
            // Dining room — a discretionary pick a struggling colony never reaches.
            { "AteWithoutTable", DefectKind.NoTable },

            { "NeedJoy", DefectKind.Cheerless },
            { "NeedBeauty", DefectKind.DrearyRoom }
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
                case DefectKind.NoTable: return RemedyKind.AddTable;
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
            if (severity <= 0f) return 0f;
            return severity * Weight(kind);
        }

        static float Weight(DefectKind kind)
        {
            switch (kind)
            {
                // A free building that removes the largest penalty in the game. Nothing else
                // here has that ratio, so nothing else should outrank it.
                case DefectKind.UnburiedDead: return 2.0f;

                case DefectKind.Overbuilt: return 1.6f;
                case DefectKind.ColdRoom: return 1.3f;   // cold kills, unlike the rest of these
                case DefectKind.NoTable: return 0.9f;    // small, but every colonist every meal
                case DefectKind.Cheerless: return 0.7f;
                case DefectKind.ExposedPowered: return 1.4f;
                case DefectKind.DarkRoom: return 1.2f;
                case DefectKind.SharedBedroom: return 1.0f;
                case DefectKind.DrearyRoom: return 0.6f;
                default: return 0.5f;
            }
        }

        /// <summary>Whether this is worth acting on at all.</summary>
        public static bool WorthActing(DefectKind kind, float severity)
        {
            return RemedyFor(kind) != RemedyKind.None && Priority(kind, severity) >= ActionThreshold;
        }
    }
}
