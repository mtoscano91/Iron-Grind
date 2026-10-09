using System;
using System.Collections.Generic;
using IronGrind.Randomness;

namespace IronGrind.Tests.EditMode.Randomness
{
    /// <summary>
    /// <see cref="IRandomProvider"/> test double that returns exact queued values (ADR-013 Decision 6).
    /// Three queues, one per method; a draw from an empty queue throws, so an unexpected draw fails the
    /// test. Returns whatever was queued, including values a real provider never produces.
    /// </summary>
    internal sealed class ScriptedRandomProvider : IRandomProvider
    {
        private readonly Queue<float> _floats = new Queue<float>();
        private readonly Queue<double> _doubles = new Queue<double>();
        private readonly Queue<int> _ints = new Queue<int>();

        /// <summary>Total number of successful draws across the three methods.</summary>
        public int DrawCount => FloatDrawCount + DoubleDrawCount + IntDrawCount;

        /// <summary>Number of successful <see cref="NextFloat"/> draws.</summary>
        public int FloatDrawCount { get; private set; }

        /// <summary>Number of successful <see cref="NextDouble"/> draws.</summary>
        public int DoubleDrawCount { get; private set; }

        /// <summary>Number of successful <see cref="NextInt"/> draws.</summary>
        public int IntDrawCount { get; private set; }

        /// <summary>Queues a value for the next <see cref="NextFloat"/> draw.</summary>
        public void EnqueueFloat(float value) => _floats.Enqueue(value);

        /// <summary>Queues a value for the next <see cref="NextDouble"/> draw.</summary>
        public void EnqueueDouble(double value) => _doubles.Enqueue(value);

        /// <summary>Queues a value for the next <see cref="NextInt"/> draw.</summary>
        public void EnqueueInt(int value) => _ints.Enqueue(value);

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The float queue is empty.</exception>
        public float NextFloat()
        {
            if (_floats.Count == 0)
                throw new InvalidOperationException("Unexpected NextFloat draw: the scripted float queue is empty.");

            FloatDrawCount++;
            return _floats.Dequeue();
        }

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">The double queue is empty.</exception>
        public double NextDouble()
        {
            if (_doubles.Count == 0)
                throw new InvalidOperationException("Unexpected NextDouble draw: the scripted double queue is empty.");

            DoubleDrawCount++;
            return _doubles.Dequeue();
        }

        /// <summary>Returns the next queued int unchanged; the range arguments are ignored.</summary>
        /// <exception cref="InvalidOperationException">The int queue is empty.</exception>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (_ints.Count == 0)
                throw new InvalidOperationException("Unexpected NextInt draw: the scripted int queue is empty.");

            IntDrawCount++;
            return _ints.Dequeue();
        }
    }
}
