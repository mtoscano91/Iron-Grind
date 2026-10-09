using System.Text.RegularExpressions;
using IronGrind.Randomness;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.Randomness
{
    /// <summary>
    /// EditMode test for <see cref="RandomProviderFactory"/> (ADR-013 Decision 4; Loot Table Story 015).
    /// The only test that calls <c>CreateSeededFromEntropy</c>; it asserts nothing about the values drawn.
    /// </summary>
    [TestFixture]
    internal sealed class RandomProviderFactory_Tests
    {
        [Test]
        public void RandomProviderFactory_CreateSeededFromEntropy_LogsSeedAndReturnsProvider()
        {
            // Arrange
            LogAssert.Expect(LogType.Log, new Regex(@"^\[Random\] PRNG seed: -?\d+$"));

            // Act
            IRandomProvider provider = RandomProviderFactory.CreateSeededFromEntropy(out int _);

            // Assert
            Assert.IsNotNull(provider);
        }
    }
}
