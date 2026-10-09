using System;
using IronGrind.Randomness;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.Randomness
{
    /// <summary>
    /// EditMode unit tests for the ADR-013 provider types (Damage Calculation Story 003): the
    /// <see cref="SystemRandomProvider"/> adapter and the shared <see cref="ScriptedRandomProvider"/> and
    /// <see cref="RecordingRandomProvider"/> doubles. Seeded tests assert properties, never exact values.
    /// </summary>
    [TestFixture]
    internal sealed class SystemRandomProvider_Tests
    {
        private const int SEED = 20261008;
        private const int RANGE_DRAW_COUNT = 1000000;
        private const int PASS_THROUGH_DRAW_COUNT = 100;
        private const int INT_MIN = 3;
        private const int INT_MAX_EXCLUSIVE = 17;
        private const int SAMPLE_BELOW_FIRST_STEP = 127;
        private const int SAMPLE_FIRST_STEP = 128;
        private const int SHIFTED_JUST_BELOW_THRESHOLD = 12582911; // 0.75 * 2^24 - 1
        private const int SHIFTED_AT_THRESHOLD = 12582912;         // 0.75 * 2^24
        private const int SHIFT_FACTOR = 128;                      // 2^7 discarded low bits
        private const float TWO_POW_MINUS_24 = 1f / 16777216f;
        private const float LARGEST_UNIT_FLOAT = 0.99999994f;
        private const float FLOAT_BELOW_THRESHOLD = 0.74999994f;
        private const float THRESHOLD = 0.75f;

        // ---------- ToUnitFloat ----------

        [Test]
        public void SystemRandomProvider_ToUnitFloat_Zero_ReturnsZero()
        {
            Assert.AreEqual(0f, SystemRandomProvider.ToUnitFloat(0), 0f);
        }

        [Test]
        public void SystemRandomProvider_ToUnitFloat_127_ReturnsZero()
        {
            Assert.AreEqual(0f, SystemRandomProvider.ToUnitFloat(SAMPLE_BELOW_FIRST_STEP), 0f);
        }

        [Test]
        public void SystemRandomProvider_ToUnitFloat_128_ReturnsTwoToMinus24()
        {
            Assert.AreEqual(TWO_POW_MINUS_24, SystemRandomProvider.ToUnitFloat(SAMPLE_FIRST_STEP), 0f);
        }

        [Test]
        public void SystemRandomProvider_ToUnitFloat_IntMaxMinusOne_ReturnsLargestFloatBelowOne()
        {
            Assert.AreEqual(LARGEST_UNIT_FLOAT, SystemRandomProvider.ToUnitFloat(int.MaxValue - 1), 0f);
        }

        [Test]
        public void SystemRandomProvider_ToUnitFloat_JustBelowThreshold_ReturnsFloatBelow075()
        {
            float result = SystemRandomProvider.ToUnitFloat(SHIFTED_JUST_BELOW_THRESHOLD * SHIFT_FACTOR);

            Assert.AreEqual(FLOAT_BELOW_THRESHOLD, result, 0f);
            Assert.Less(result, THRESHOLD);
        }

        [Test]
        public void SystemRandomProvider_ToUnitFloat_AtThreshold_ReturnsExactly075()
        {
            Assert.AreEqual(THRESHOLD, SystemRandomProvider.ToUnitFloat(SHIFTED_AT_THRESHOLD * SHIFT_FACTOR), 0f);
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void SystemRandomProvider_ToUnitFloat_NegativeSample_Throws(int sample)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SystemRandomProvider.ToUnitFloat(sample));
        }

        // ---------- adapter ----------

        [Test]
        public void SystemRandomProvider_NextFloat_SeededRun_AlwaysInZeroInclusiveToOneExclusive()
        {
            // Arrange
            var provider = new SystemRandomProvider(new System.Random(SEED));
            int outOfRange = 0;

            // Act
            for (int i = 0; i < RANGE_DRAW_COUNT; i++)
            {
                float value = provider.NextFloat();
                if (!(value >= 0f && value < 1f))
                    outOfRange++;
            }

            // Assert
            Assert.AreEqual(0, outOfRange);
        }

        [Test]
        public void SystemRandomProvider_NextFloat_SameSeed_EqualsToUnitFloatOfOneIntegerDraw()
        {
            // Arrange - a second generator with the same seed stays in step only if each float uses one Next()
            var provider = new SystemRandomProvider(new System.Random(SEED));
            var reference = new System.Random(SEED);

            // Act / Assert
            for (int i = 0; i < PASS_THROUGH_DRAW_COUNT; i++)
                Assert.AreEqual(SystemRandomProvider.ToUnitFloat(reference.Next()), provider.NextFloat(), 0f);
        }

        [Test]
        public void SystemRandomProvider_NextDouble_SameSeed_ReturnsWhatSystemRandomReturns()
        {
            // Arrange
            var provider = new SystemRandomProvider(new System.Random(SEED));
            var reference = new System.Random(SEED);

            // Act / Assert
            for (int i = 0; i < PASS_THROUGH_DRAW_COUNT; i++)
                Assert.AreEqual(reference.NextDouble(), provider.NextDouble(), 0.0);
        }

        [Test]
        public void SystemRandomProvider_NextInt_SameSeed_ReturnsWhatSystemRandomReturns()
        {
            // Arrange
            var provider = new SystemRandomProvider(new System.Random(SEED));
            var reference = new System.Random(SEED);

            // Act / Assert
            for (int i = 0; i < PASS_THROUGH_DRAW_COUNT; i++)
                Assert.AreEqual(reference.Next(INT_MIN, INT_MAX_EXCLUSIVE), provider.NextInt(INT_MIN, INT_MAX_EXCLUSIVE));
        }

        [Test]
        public void SystemRandomProvider_NextInt_EqualBounds_ReturnsMin()
        {
            var provider = new SystemRandomProvider(new System.Random(SEED));

            Assert.AreEqual(INT_MIN, provider.NextInt(INT_MIN, INT_MIN));
        }

        [Test]
        public void SystemRandomProvider_NextInt_MinAboveMax_Throws()
        {
            var provider = new SystemRandomProvider(new System.Random(SEED));

            Assert.Throws<ArgumentOutOfRangeException>(() => provider.NextInt(INT_MAX_EXCLUSIVE, INT_MIN));
        }

        [Test]
        public void SystemRandomProvider_Constructor_NullRandom_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new SystemRandomProvider(null));
            Assert.AreEqual("random", ex.ParamName);
        }
    }

    /// <summary>EditMode tests for the shared <see cref="ScriptedRandomProvider"/> double (ADR-013 Decision 6).</summary>
    [TestFixture]
    internal sealed class ScriptedRandomProvider_Tests
    {
        private const float FIRST_FLOAT = 0.25f;
        private const float SECOND_FLOAT = 1.0f; // a value a real provider never produces
        private const double FIRST_DOUBLE = 0.125;
        private const int FIRST_INT = 42;
        private const int IGNORED_MIN = 0;
        private const int IGNORED_MAX = 3;

        [Test]
        public void ScriptedRandomProvider_NextFloat_ReturnsQueuedValuesUnchangedInOrder()
        {
            var provider = new ScriptedRandomProvider();
            provider.EnqueueFloat(FIRST_FLOAT);
            provider.EnqueueFloat(SECOND_FLOAT);

            Assert.AreEqual(FIRST_FLOAT, provider.NextFloat(), 0f);
            Assert.AreEqual(SECOND_FLOAT, provider.NextFloat(), 0f);
        }

        [Test]
        public void ScriptedRandomProvider_NextDouble_ReturnsQueuedValueUnchanged()
        {
            var provider = new ScriptedRandomProvider();
            provider.EnqueueDouble(FIRST_DOUBLE);

            Assert.AreEqual(FIRST_DOUBLE, provider.NextDouble(), 0.0);
        }

        [Test]
        public void ScriptedRandomProvider_NextInt_ReturnsQueuedValueIgnoringRange()
        {
            var provider = new ScriptedRandomProvider();
            provider.EnqueueInt(FIRST_INT);

            Assert.AreEqual(FIRST_INT, provider.NextInt(IGNORED_MIN, IGNORED_MAX));
        }

        [Test]
        public void ScriptedRandomProvider_QueuesAreIndependent()
        {
            var provider = new ScriptedRandomProvider();
            provider.EnqueueFloat(FIRST_FLOAT);

            Assert.Throws<InvalidOperationException>(() => provider.NextDouble());
            Assert.Throws<InvalidOperationException>(() => provider.NextInt(IGNORED_MIN, IGNORED_MAX));
            Assert.AreEqual(FIRST_FLOAT, provider.NextFloat(), 0f);
        }

        [Test]
        public void ScriptedRandomProvider_EmptyQueue_Throws()
        {
            var provider = new ScriptedRandomProvider();

            Assert.Throws<InvalidOperationException>(() => provider.NextFloat());
            Assert.Throws<InvalidOperationException>(() => provider.NextDouble());
            Assert.Throws<InvalidOperationException>(() => provider.NextInt(IGNORED_MIN, IGNORED_MAX));
        }

        [Test]
        public void ScriptedRandomProvider_Draws_CountersAreCorrect()
        {
            // Arrange
            var provider = new ScriptedRandomProvider();
            provider.EnqueueFloat(FIRST_FLOAT);
            provider.EnqueueFloat(SECOND_FLOAT);
            provider.EnqueueDouble(FIRST_DOUBLE);
            provider.EnqueueInt(FIRST_INT);

            // Act
            provider.NextFloat();
            provider.NextFloat();
            provider.NextDouble();
            provider.NextInt(IGNORED_MIN, IGNORED_MAX);

            // Assert
            Assert.AreEqual(4, provider.DrawCount);
            Assert.AreEqual(2, provider.FloatDrawCount);
            Assert.AreEqual(1, provider.DoubleDrawCount);
            Assert.AreEqual(1, provider.IntDrawCount);
        }
    }

    /// <summary>EditMode tests for the shared <see cref="RecordingRandomProvider"/> double (ADR-013 Decision 6).</summary>
    [TestFixture]
    internal sealed class RecordingRandomProvider_Tests
    {
        private const float FIRST_FLOAT = 0.5f;
        private const float SECOND_FLOAT = 0.375f;
        private const double FIRST_DOUBLE = 0.0625;
        private const int FIRST_INT = 7;
        private const int IGNORED_MIN = 0;
        private const int IGNORED_MAX = 10;

        [Test]
        public void RecordingRandomProvider_Draws_ForwardsInnerValuesAndKeepsLastPerMethod()
        {
            // Arrange
            var inner = new ScriptedRandomProvider();
            inner.EnqueueFloat(FIRST_FLOAT);
            inner.EnqueueFloat(SECOND_FLOAT);
            inner.EnqueueDouble(FIRST_DOUBLE);
            inner.EnqueueInt(FIRST_INT);
            var recording = new RecordingRandomProvider(inner);

            // Act
            float firstFloat = recording.NextFloat();
            float secondFloat = recording.NextFloat();
            double doubleValue = recording.NextDouble();
            int intValue = recording.NextInt(IGNORED_MIN, IGNORED_MAX);

            // Assert
            Assert.AreEqual(FIRST_FLOAT, firstFloat, 0f);
            Assert.AreEqual(SECOND_FLOAT, secondFloat, 0f);
            Assert.AreEqual(FIRST_DOUBLE, doubleValue, 0.0);
            Assert.AreEqual(FIRST_INT, intValue);
            Assert.AreEqual(SECOND_FLOAT, recording.LastFloat, 0f);
            Assert.AreEqual(FIRST_DOUBLE, recording.LastDouble, 0.0);
            Assert.AreEqual(FIRST_INT, recording.LastInt);
        }

        [Test]
        public void RecordingRandomProvider_Draws_CountersAreCorrectAndInnerSeesSameDraws()
        {
            // Arrange
            var inner = new ScriptedRandomProvider();
            inner.EnqueueFloat(FIRST_FLOAT);
            inner.EnqueueDouble(FIRST_DOUBLE);
            inner.EnqueueDouble(FIRST_DOUBLE);
            var recording = new RecordingRandomProvider(inner);

            // Act
            recording.NextFloat();
            recording.NextDouble();
            recording.NextDouble();

            // Assert
            Assert.AreEqual(3, recording.DrawCount);
            Assert.AreEqual(1, recording.FloatDrawCount);
            Assert.AreEqual(2, recording.DoubleDrawCount);
            Assert.AreEqual(0, recording.IntDrawCount);
            Assert.AreEqual(3, inner.DrawCount);
        }

        [Test]
        public void RecordingRandomProvider_InnerThrows_ExceptionPropagates()
        {
            var recording = new RecordingRandomProvider(new ScriptedRandomProvider());

            Assert.Throws<InvalidOperationException>(() => recording.NextFloat());
        }

        [Test]
        public void RecordingRandomProvider_Constructor_NullInner_Throws()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => new RecordingRandomProvider(null));
            Assert.AreEqual("inner", ex.ParamName);
        }
    }
}
