using System;
using AutoColony.Learning;
using Xunit;

namespace AutoColony.Tests
{
    public class AcRandomTests
    {
        [Fact]
        public void SameSeedProducesSameSequence()
        {
            // This is the property the trial harness depends on: two runs seeded alike must
            // make identical decisions, or paired comparison means nothing.
            var a = new AcRandom(12345);
            var b = new AcRandom(12345);

            for (int i = 0; i < 1000; i++)
                Assert.Equal(a.NextULong(), b.NextULong());
        }

        [Fact]
        public void DifferentSeedsDiverge()
        {
            var a = new AcRandom(1);
            var b = new AcRandom(2);

            int same = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextULong() == b.NextULong()) same++;

            Assert.True(same < 5, "sequences from different seeds should not track each other");
        }

        [Fact]
        public void ValueStaysInUnitInterval()
        {
            var rng = new AcRandom(999);
            for (int i = 0; i < 20000; i++)
            {
                float v = rng.Value;
                Assert.InRange(v, 0f, 0.9999999f);
            }
        }

        [Fact]
        public void ValueIsRoughlyUniform()
        {
            var rng = new AcRandom(7);
            var buckets = new int[10];
            const int n = 100000;

            for (int i = 0; i < n; i++)
            {
                int b = (int)(rng.Value * 10f);
                if (b > 9) b = 9;
                buckets[b]++;
            }

            // Each decile should hold ~10% of samples; allow a generous 2% absolute band.
            foreach (var count in buckets)
                Assert.InRange(count / (double)n, 0.08, 0.12);
        }

        [Fact]
        public void RangeRespectsBounds()
        {
            var rng = new AcRandom(31337);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.Range(-5, 5);
                Assert.InRange(v, -5, 4);
            }
        }

        [Fact]
        public void RangeWithEmptySpanReturnsMinimum()
        {
            var rng = new AcRandom(1);
            Assert.Equal(3, rng.Range(3, 3));
            Assert.Equal(3, rng.Range(3, 1));
        }

        [Fact]
        public void GaussianHasExpectedMomentsy()
        {
            var rng = new AcRandom(2024);
            const int n = 200000;
            double sum = 0, sumSq = 0;

            for (int i = 0; i < n; i++)
            {
                double g = rng.Gaussian();
                sum += g;
                sumSq += g * g;
            }

            double mean = sum / n;
            double variance = sumSq / n - mean * mean;

            Assert.InRange(mean, -0.02, 0.02);
            Assert.InRange(Math.Sqrt(variance), 0.97, 1.03);
        }
    }
}
