using System;
using AutoColony.Learning;
using Verse;

namespace AutoColony
{
    /// <summary>Everything a module needs to make one decision pass.</summary>
    public class DirectorContext
    {
        public Map map;
        public ColonyState state;
        public StrategyGenome genome;
        public AutoColonyDirector director;
        public BaseLayout layout;

        /// <summary>
        /// What the colony is working towards this pass. Modules aim at it rather than each
        /// forming its own opinion about what matters.
        /// </summary>
        public Goals.ColonyPlan plan;

        /// <summary>
        /// Where the colony is, for anything that measures distance from it.
        ///
        /// The fallback to map centre was written out at eight call sites and three of them had
        /// already dropped the `layout != null` guard — the kind of drift a shared accessor
        /// makes impossible.
        /// </summary>
        public Verse.IntVec3 Origin
        {
            get
            {
                return layout != null && layout.established ? layout.origin : map.Center;
            }
        }

        public float Gene(string key)
        {
            return genome != null ? genome.Get(key) : 0f;
        }

        public int GeneInt(string key)
        {
            return genome != null ? genome.GetInt(key) : 0;
        }

        /// <summary>
        /// Records that a discrete choice was made, so the epoch's fitness can be credited
        /// back to it when the epoch closes. See <see cref="AutoColonyDirector.CreditLater"/>.
        /// </summary>
        public void Credit(string banditId, string arm)
        {
            if (director != null) director.CreditLater(banditId, arm);
        }

        /// <summary>
        /// Days of food this colony wants right now, season included.
        ///
        /// Every module that stocks, grows, gathers or buys food asks this rather than reading
        /// the gene directly, so the nine of them cannot disagree about the answer. Run 159
        /// bought exactly what the flat gene asked for, four days before winter, and starved
        /// two days later. See FoodTarget.
        /// </summary>
        /// <summary>
        /// Days of food the colony will still have when it comes to eat it.
        ///
        /// daysOfFood counts everything reachable and edible, which is the right answer to "is
        /// there food" and the wrong one to "are we secure". Run 168 held 15.0 days of which 7.2
        /// were spoiling; across four runs the spoiling share has been a third to a half.
        /// ColonyState measures it and exactly one decision — the refrigeration goal's urgency —
        /// has ever read it, against forty-three that read the gross number.
        ///
        /// The subtraction is not the whole spoiling figure, and getting that wrong was nearly
        /// shipped. "Spoiling" means rots inside SpoilingSoonDays, which is food that must be
        /// eaten soon rather than food already lost — most of it is edible today. What is
        /// actually lost is only what cannot be consumed before it rots, so the colony keeps the
        /// horizon's worth of it and loses the rest.
        /// </summary>
        public float DaysOfFoodKeeping
        {
            get
            {
                if (state == null) return 0f;

                float lost = state.daysOfFoodSpoiling - ColonyState.SpoilingSoonDays;
                if (lost < 0f) lost = 0f;

                float keeping = state.daysOfFood - lost;
                return keeping < 0f ? 0f : keeping;
            }
        }

        public float FoodDaysWanted
        {
            get
            {
                // The margin is a ratio, and it wants a gene that is one.
                //
                // This passed Genes.GrowthFoodMargin for two sessions, which is "days of food
                // before another mouth is wanted" — a days count, range 4 to 10, used correctly
                // as days by GoalSet and used here as a multiplier. Run 167 opened on 25 growing
                // days against 25 barren and asked for 150 days of food.
                //
                // Nothing caught it because the arithmetic was tested with the number it should
                // have been given: FoodTargetTests passes 1.3 throughout, so the tests encoded
                // the right intent while the wiring supplied something else. It surfaced the
                // first time a map put the growing season below the barren one, and only because
                // the answer had just been made visible in the chronicle.
                //
                // The same duplicated-quantity fault this class exists to fix, committed in the
                // fix. Two meanings, one gene.
                return FoodTarget.Days(
                    Gene(Learning.Genes.FoodDaysPerColonist),
                    state != null ? state.growingDaysLeft : 0,
                    state != null ? state.barrenDaysAhead : 0,
                    Gene(Learning.Genes.FoodWinterMargin));
            }
        }

    }

    /// <summary>
    /// One area of colony management the director can act on.
    ///
    /// Modules are scheduled round-robin rather than all running each tick, and each one is
    /// individually fault-isolated: a module that throws repeatedly is disabled instead of
    /// taking the game down with it, since this code runs unattended for hours at a time.
    /// </summary>
    public abstract class DirectorModule
    {
        /// <summary>Failures tolerated before the module is switched off for the session.</summary>
        public const int MaxFailures = 5;

        public abstract string Name { get; }

        /// <summary>How often this module wants to act. 2500 ticks is one in-game hour.</summary>
        public virtual int IntervalTicks { get { return 2500; } }

        /// <summary>
        /// How often it acts while something it can answer is actually happening.
        ///
        /// The director's responsiveness used to be a constant. Every module ran on a fixed
        /// interval whatever the colony was doing, so the work module — the one that raises
        /// Doctor to 4x the moment anyone goes down — applied that decision up to three in-game
        /// hours after the person hit the floor, and the resource module stopped a hunt up to
        /// five hours after it should have. Measured directly: a colonist went down at 14h and
        /// gathering was still not held off until 18h.
        ///
        /// The round-robin was wrongly blamed for that. Advancing the cursor costs about fifteen
        /// ticks to sweep every module, a quarter of a second; the delay was entirely each
        /// module's own interval. So the fix belongs here rather than in the scheduler, and one
        /// module still runs per tick, which keeps the flat per-tick cost intact.
        /// </summary>
        public virtual int UrgentIntervalTicks { get { return 600; } }

        /// <summary>
        /// Whether the colony is in a state this module needs to answer now rather than on its
        /// ordinary schedule. False by default: most work genuinely can wait, and a module that
        /// declares itself urgent in ordinary conditions simply burns ticks other modules need.
        /// </summary>
        public virtual bool Urgent(DirectorContext ctx) { return false; }

        /// <summary>Modules that only matter once a colony exists can skip early ticks.</summary>
        public virtual bool RequiresColonists { get { return true; } }

        /// <summary>
        /// Whether this module's work can wait while something is on fire or shooting.
        ///
        /// Declared rather than remembered: three modules had copied the same pair of guards
        /// into the top of their own Act, and they had already drifted apart.
        /// </summary>
        public virtual bool Discretionary { get { return false; } }

        public bool enabled = true;
        public int failures;
        public int lastRunTick = -999999;
        public string lastAction = "idle";
        public int actionsTaken;

        protected abstract void Act(DirectorContext ctx);

        public bool ShouldRun(int tick)
        {
            return ShouldRun(tick, null);
        }

        public bool ShouldRun(int tick, DirectorContext ctx)
        {
            if (!enabled || failures >= MaxFailures) return false;

            int interval = IntervalTicks;

            // Urgency may only ever make a module run sooner, never later. Written the other way
            // round it would be a second schedule to keep in step with the first.
            if (ctx != null && ctx.state != null && ctx.state.Valid)
            {
                bool urgent;
                try { urgent = Urgent(ctx); }
                catch (Exception) { urgent = false; }   // a broken test must not stop the module

                if (urgent && UrgentIntervalTicks < interval) interval = UrgentIntervalTicks;
            }

            return tick - lastRunTick >= interval;
        }

        /// <summary>Runs the module, absorbing any exception it raises.</summary>
        public void Run(DirectorContext ctx, int tick)
        {
            lastRunTick = tick;
            if (RequiresColonists && (ctx.state == null || !ctx.state.Valid)) return;
            if (Discretionary && Deferred(ctx)) return;

            try
            {
                Act(ctx);
            }
            catch (Exception e)
            {
                failures++;
                AcLog.Error("Module '" + Name + "' failed (" + failures + "/" + MaxFailures + "): " + e);
                if (failures >= MaxFailures)
                    AcLog.Error("Module '" + Name + "' disabled for this session after repeated failures.");
            }
        }

        /// <summary>True while the colony has something more pressing than this module.</summary>
        static bool Deferred(DirectorContext ctx)
        {
            if (ctx.state.EmergencyAtHome) return true;
            return ctx.plan != null && ctx.plan.EmergencyActive;
        }

        protected void Note(string what)
        {
            lastAction = what;
            actionsTaken++;
            AcLog.Verbose(Name + ": " + what);
        }

        public void ResetRuntimeState()
        {
            failures = 0;
            lastRunTick = -999999;
        }
    }
}
