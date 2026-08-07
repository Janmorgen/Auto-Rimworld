using System.Collections.Generic;

namespace AutoColony
{
    /// <summary>
    /// Whether two parts of the director are sawing at the same thing.
    ///
    /// The bed has now been built and pulled back out about twice a day in three separate runs,
    /// and has been "fixed" twice. Both fixes had the same shape — find the attribute the two
    /// sides disagree about and make them read it from one place. First the *number*: the planner
    /// topped a bedroom up to BedsPerRoom while the upkeep survey drove it toward a single bed.
    /// Then the *region*: they agreed on the number and still counted it over different areas,
    /// the planner over PlannedRoom.Rect and upkeep over RimWorld's own Room, which are the same
    /// only for a finished sealed room. Each fix was correct and each was followed by a third
    /// recurrence, because the supply of attributes to disagree about is not something anybody
    /// enumerated — run 195 stood at `1 of 4 beds are inside a room`, so most of them were not in
    /// any region either side was counting over.
    ///
    /// So this stops chasing the next attribute. It does not know what a bed is, which module
    /// placed it, or which of the two is right — it knows only that something was added here and
    /// then removed here and then added here again, and that a colony paying construction work
    /// for that is worse off than a colony that simply stopped.
    ///
    /// The general claim, which is the reason this is not written into the bed code:
    ///
    ///     any two modules that can both write the same effect can deadlock, and
    ///     the ones already known are only the ones that happened to be visible.
    ///
    /// `Touches.ContestedEffects()` lists seven effects with more than one writer. This is the
    /// instrument for the failure mode all seven share, and it will fire on whichever pair gets
    /// there first without anybody having predicted the pair.
    ///
    /// **Reversals, not actions.** Building a bed and taking it out a week later is a colony
    /// changing its mind, which is allowed and often right. Building and removing the same bed in
    /// the same room four times in two days is a fight. Only direction changes are counted, and
    /// only recent ones — a quiet spell forgets, so a legitimate rebuild months later starts
    /// clean rather than inheriting an old argument.
    ///
    /// **It decides nothing.** It reports a count and how long the argument has run; the module
    /// about to act decides whether to stand down and says why in its own words. That is the same
    /// separation `CapabilityGaps` keeps, and for the same reason: the last several instruments
    /// added alongside a decision each changed what the decision should be.
    ///
    /// Free of game types so the argument can be had in a test.
    /// </summary>
    public static class Churn
    {
        /// <summary>One running argument about one thing in one place.</summary>
        public class Fight
        {
            /// <summary>What is being fought over — "Bed", "Wall".</summary>
            public string what;

            /// <summary>Where, in whatever key the caller uses. Cells and rooms both work.</summary>
            public int where;

            /// <summary>Which way the last write went. True was an addition.</summary>
            public bool lastAdded;

            /// <summary>How many times the direction has changed inside the memory window.</summary>
            public int reversals;

            /// <summary>Tick of the first write in the current spell.</summary>
            public int openedAt;

            /// <summary>Tick of the most recent write.</summary>
            public int lastSeen;
        }

        static readonly Dictionary<string, Fight> fights = new Dictionary<string, Fight>();

        /// <summary>Ticks in a RimWorld day. Same number `CapabilityGaps` divides by.</summary>
        public const float TicksPerDay = 60000f;

        /// <summary>
        /// The memory window in ticks, from a window in days.
        ///
        /// Here rather than at the call sites so the two sides of an argument cannot end up
        /// forgetting it on different schedules — one side still counting while the other has
        /// moved on is a quieter version of the same disagreement.
        /// </summary>
        public static int MemoryTicks(float days)
        {
            if (days <= 0f) return 0;
            return (int)(days * TicksPerDay);
        }

        static string Key(string what, int where)
        {
            return what + "@" + where;
        }

        /// <summary>Forget everything. For tests and for a fresh colony.</summary>
        public static void Clear() { fights.Clear(); }

        /// <summary>
        /// Note that something was put here or taken from here.
        ///
        /// Cheap enough to call unconditionally on every write, which matters: an instrument that
        /// has to be remembered at the call site is one that will be missing from the pair that
        /// eventually deadlocks. Repeating the same direction is not a reversal, so a planner
        /// topping a room up every pass costs one comparison and changes nothing.
        /// </summary>
        public static void Record(string what, int where, bool added, int nowTick, int memoryTicks)
        {
            if (string.IsNullOrEmpty(what)) return;

            string key = Key(what, where);
            Fight f;
            if (!fights.TryGetValue(key, out f))
            {
                fights[key] = new Fight
                {
                    what = what,
                    where = where,
                    lastAdded = added,
                    reversals = 0,
                    openedAt = nowTick,
                    lastSeen = nowTick
                };
                return;
            }

            // A quiet spell ends the argument. Whatever the two sides were disagreeing about,
            // they have not acted on it for long enough that this is a new decision rather than
            // the continuation of an old one.
            if (memoryTicks > 0 && nowTick - f.lastSeen > memoryTicks)
            {
                f.reversals = 0;
                f.openedAt = nowTick;
            }
            else if (added != f.lastAdded)
            {
                f.reversals++;
            }

            f.lastAdded = added;
            f.lastSeen = nowTick;
        }

        /// <summary>
        /// How many times the direction has changed here recently, or 0 if nothing has.
        ///
        /// Reads without recording, so a module can ask before it acts.
        /// </summary>
        public static int Reversals(string what, int where, int nowTick, int memoryTicks)
        {
            Fight f;
            if (string.IsNullOrEmpty(what) || !fights.TryGetValue(Key(what, where), out f))
                return 0;

            if (memoryTicks > 0 && nowTick - f.lastSeen > memoryTicks) return 0;
            return f.reversals;
        }

        /// <summary>
        /// Whether this has flipped more times than the colony should pay for.
        ///
        /// A tolerance of zero would forbid ever changing one's mind, so the caller supplies it
        /// and the sensible values start at one — one reversal is a correction, and the second is
        /// the first evidence that the correction is not holding.
        /// </summary>
        public static bool IsSawing(string what, int where, int tolerance, int nowTick,
                                    int memoryTicks)
        {
            if (tolerance < 0) tolerance = 0;
            return Reversals(what, where, nowTick, memoryTicks) > tolerance;
        }

        /// <summary>How long the current argument has run, in ticks, or -1 if there is none.</summary>
        public static int StandingFor(string what, int where, int nowTick, int memoryTicks)
        {
            Fight f;
            if (string.IsNullOrEmpty(what) || !fights.TryGetValue(Key(what, where), out f))
                return -1;

            if (memoryTicks > 0 && nowTick - f.lastSeen > memoryTicks) return -1;

            int age = nowTick - f.openedAt;
            return age < 0 ? 0 : age;
        }

        /// <summary>Everything currently being fought over, worst first.</summary>
        public static List<Fight> All(int nowTick, int memoryTicks)
        {
            var live = new List<Fight>();
            foreach (var f in fights.Values)
            {
                if (memoryTicks > 0 && nowTick - f.lastSeen > memoryTicks) continue;
                if (f.reversals > 0) live.Add(f);
            }
            live.Sort((a, b) => b.reversals.CompareTo(a.reversals));
            return live;
        }

        /// <summary>One line naming the argument, for the module that is about to stand down.</summary>
        public static string Explain(string what, int where, int nowTick, int memoryTicks)
        {
            int flips = Reversals(what, where, nowTick, memoryTicks);
            if (flips <= 0) return what + " is not being fought over";

            int age = StandingFor(what, where, nowTick, memoryTicks);
            return what + " here has changed hands " + flips + " times in " +
                   (age / 60000f).ToString("0.0") + " days";
        }
    }
}
