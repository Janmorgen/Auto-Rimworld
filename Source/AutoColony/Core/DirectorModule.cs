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
            return enabled && failures < MaxFailures && tick - lastRunTick >= IntervalTicks;
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
