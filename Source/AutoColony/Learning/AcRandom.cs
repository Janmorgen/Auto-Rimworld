using System;
using Verse;

namespace AutoColony.Learning
{
    /// <summary>
    /// A deterministic PRNG owned by the learning layer.
    ///
    /// The learning code deliberately does not use <c>Verse.Rand</c>. Drawing from RimWorld's
    /// global stream would advance it by an amount that depends on how many mutations and
    /// bandit ties happened to occur, which perturbs every subsequent world roll — weather,
    /// raids, trader stock — relative to an unmodded game. That both changes the game the
    /// player is playing and makes two runs from the same save incomparable, which is exactly
    /// what the trial harness needs to rely on.
    ///
    /// Algorithm is splitmix64: one 64-bit word of state, good statistical quality, and
    /// trivially serialisable so a save resumes the same sequence.
    /// </summary>
    public class AcRandom : IExposable
    {
        ulong state;

        public AcRandom() : this(0x1234ABCDu) { }

        public AcRandom(ulong seed)
        {
            state = seed;
        }

        public void Reseed(ulong seed)
        {
            state = seed;
        }

        public ulong NextULong()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform in [0,1). Uses 24 bits, which is all a float can represent anyway.</summary>
        public float Value
        {
            get { return (NextULong() >> 40) / 16777216f; }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>Standard normal sample via Box-Muller.</summary>
        public double Gaussian()
        {
            double u1 = 1.0 - Value;
            double u2 = 1.0 - Value;
            if (u1 < 1e-9) u1 = 1e-9;
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        public void ExposeData()
        {
            // Split across two ints: Scribe handles int everywhere, ulong is not guaranteed.
            int hi = unchecked((int)(state >> 32));
            int lo = unchecked((int)(state & 0xFFFFFFFFUL));
            Scribe_Values.Look(ref hi, "stateHi", 0);
            Scribe_Values.Look(ref lo, "stateLo", 0);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                state = ((ulong)(uint)hi << 32) | (uint)lo;
        }
    }
}
