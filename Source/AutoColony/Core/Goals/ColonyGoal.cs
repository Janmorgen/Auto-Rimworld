using System.Collections.Generic;

namespace AutoColony.Goals
{
    /// <summary>
    /// How far out a goal pays off, which decides what may interrupt what.
    /// </summary>
    public enum GoalHorizon
    {
        /// <summary>Happening now and cannot wait: fire, a raid, an empty larder.</summary>
        Immediate = 0,

        /// <summary>This season. Beds, food stocks, a roof over the stockpile.</summary>
        ShortTerm = 1,

        /// <summary>This year. Power, refrigeration, real defences — none of it urgent, all of it compounding.</summary>
        LongTerm = 2
    }

    /// <summary>Materials a goal needs before it can be attempted.</summary>
    public class MaterialNeeds
    {
        readonly Dictionary<string, int> needs = new Dictionary<string, int>();

        public void Need(string thingDefName, int amount)
        {
            if (string.IsNullOrEmpty(thingDefName) || amount <= 0) return;
            int current;
            needs.TryGetValue(thingDefName, out current);
            if (amount > current) needs[thingDefName] = amount;
        }

        public int For(string thingDefName)
        {
            int amount;
            return needs.TryGetValue(thingDefName, out amount) ? amount : 0;
        }

        public bool Any { get { return needs.Count > 0; } }

        public IEnumerable<KeyValuePair<string, int>> All { get { return needs; } }

        public void Clear() { needs.Clear(); }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in needs)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Value).Append(' ').Append(kv.Key);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// One thing the colony is trying to achieve.
    ///
    /// Goals exist because the modules underneath them are individually sensible and
    /// collectively aimless. Each knows how to build a room or mine a rock; none of them knows
    /// that a freezer needs power, that power needs components, that components need mining,
    /// and that none of it matters while the kitchen is on fire. Stating the dependencies
    /// declaratively lets the planner work backwards from what the colony wants to whatever
    /// can actually be done about it today.
    /// </summary>
    public abstract class ColonyGoal
    {
        public abstract string Name { get; }
        public abstract GoalHorizon Horizon { get; }

        /// <summary>Names of goals that must be satisfied before this one can be attempted.</summary>
        public virtual string[] Requires { get { return NoPrerequisites; } }

        protected static readonly string[] NoPrerequisites = new string[0];

        /// <summary>Whether the colony already has this.</summary>
        public abstract bool Satisfied(DirectorContext ctx);

        /// <summary>
        /// How badly this is wanted right now, 0 to 1. Used to order goals within a horizon,
        /// so a colony three days from starving outranks one that merely wants more beds.
        /// </summary>
        public abstract float Urgency(DirectorContext ctx);

        /// <summary>Materials that must be in hand before this goal can proceed.</summary>
        public virtual void DeclareNeeds(DirectorContext ctx, MaterialNeeds needs) { }

        /// <summary>A room this goal wants reserved and built, if any.</summary>
        public virtual RoomRole? WantsRoom { get { return null; } }

        /// <summary>One line explaining the current state, for the chronicle.</summary>
        public virtual string Explain(DirectorContext ctx) { return Name; }
    }
}
