namespace AutoColony
{
    /// <summary>
    /// The small arithmetic the whole mod leans on, in one place and free of UnityEngine.
    ///
    /// `Mathf` would do all of this, but it lives in an assembly the offline tests cannot load —
    /// reference assemblies carry no method bodies — so anything compiled into the test project
    /// has to avoid it. That constraint is real, and it quietly produced nineteen hand-written
    /// copies of the same four functions: `Clamp01` alone was declared thirteen times, six of
    /// them in a single file, one per goal class.
    /// </summary>
    public static class AcMath
    {
        public static float Clamp(float v, float min, float max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public static float Clamp01(float v)
        {
            return Clamp(v, 0f, 1f);
        }

        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }

        public static int Round(float v) { return (int)(v >= 0f ? v + 0.5f : v - 0.5f); }

        public static float Sqrt(float v) { return (float)System.Math.Sqrt(v); }
    }
}
