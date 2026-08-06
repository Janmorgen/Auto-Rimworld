using System.Collections.Generic;

namespace AutoColony.Goals
{
    /// <summary>
    /// How fast this colony actually does things, measured on itself.
    ///
    /// Patience needs a rate, and there are two ways to get one. The rate could be looked up —
    /// bench work speed times researcher skill times a difficulty factor — which is a table
    /// somebody has to write and keep true, and which is wrong the moment the researcher is
    /// hauling instead. Or it can be read off the quantity the colony is already tracking, by
    /// watching how fast the number moves.
    ///
    /// The second is both less work and more honest. A colony whose builders are all drafted
    /// measures a construction rate of zero without anybody having to tell it that drafting
    /// costs building; a colony whose only researcher is having a mental break measures no
    /// research. Every reason a rate might drop is folded in for free, including reasons
    /// nobody thought of.
    ///
    /// One rule matters more than the arithmetic: <b>the first reading after a reset is a
    /// baseline, not a rate.</b> The planner keeps no state across a save reload, so the meter
    /// starts empty every time a game is loaded. Without this rule the first sample would
    /// difference against a zero baseline and report an enormous rate, and the colony would
    /// conclude it could research anything in an afternoon.
    ///
    /// Free of game types on purpose.
    /// </summary>
    public class ColonyPace
    {
        /// <summary>What one meter remembers between readings.</summary>
        class Meter
        {
            public float lastValue;
            public int lastTick = -1;
            public bool primed;
        }

        readonly Dictionary<string, Meter> meters = new Dictionary<string, Meter>();

        /// <summary>
        /// Feed a monotone cumulative quantity and get back units per tick, or
        /// <see cref="GoalPatience.NotDerivable"/>'s float equivalent — a rate of zero, which
        /// the patience arithmetic already treats as "no honest estimate".
        ///
        /// <paramref name="cumulative"/> must only ever rise, which is true of research points
        /// banked. For a quantity that falls as work is done — the remaining-work pile on a
        /// construction site — pass its negation through <see cref="Drain"/> instead.
        /// </summary>
        public float Rate(string name, float cumulative, int nowTick)
        {
            var meter = MeterFor(name);

            // First sighting, or a clock that has gone backwards because a save was loaded.
            // Take the baseline and report nothing; a rate needs two points.
            if (!meter.primed || nowTick <= meter.lastTick)
            {
                meter.lastValue = cumulative;
                meter.lastTick = nowTick;
                meter.primed = true;
                return 0f;
            }

            int elapsed = nowTick - meter.lastTick;
            float gained = cumulative - meter.lastValue;

            meter.lastValue = cumulative;
            meter.lastTick = nowTick;

            // A cumulative quantity that fell is a new colony wearing the old one's meter, or a
            // project abandoned. Either way the difference is not a rate.
            if (gained <= 0f || elapsed <= 0) return 0f;

            return gained / elapsed;
        }

        /// <summary>
        /// The same measurement for a pile that shrinks as it is worked — remaining
        /// construction, rather than points banked. Returns how fast it is going down.
        /// </summary>
        public float Drain(string name, float remaining, int nowTick)
        {
            var meter = MeterFor(name);

            if (!meter.primed || nowTick <= meter.lastTick)
            {
                meter.lastValue = remaining;
                meter.lastTick = nowTick;
                meter.primed = true;
                return 0f;
            }

            int elapsed = nowTick - meter.lastTick;
            float spent = meter.lastValue - remaining;   // falling pile, so this is progress

            meter.lastValue = remaining;
            meter.lastTick = nowTick;

            // The pile grew: the colony queued more work than it finished. That is real and
            // common, and it is not a rate at which the pile will clear.
            if (spent <= 0f || elapsed <= 0) return 0f;

            return spent / elapsed;
        }

        /// <summary>How many readings this meter has taken, for reporting how much to trust it.</summary>
        public bool HasReading(string name)
        {
            Meter meter;
            return meters.TryGetValue(name, out meter) && meter.primed;
        }

        Meter MeterFor(string name)
        {
            Meter meter;
            if (!meters.TryGetValue(name, out meter))
            {
                meter = new Meter();
                meters[name] = meter;
            }
            return meter;
        }

        /// <summary>For the self-test, and for starting a colony clean.</summary>
        public void Clear() { meters.Clear(); }
    }
}
