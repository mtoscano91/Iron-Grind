using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Smoke test for the gateway skeleton (ADR-016 Migration Plan step 4): proves the
/// solution builds, the test project references the gateway, and CI runs NUnit.
/// </summary>
[TestFixture]
public class GatewaySkeletonBuildTest
{
    [Test]
    public void test_gateway_assembly_loads_has_entry_point()
    {
        var assembly = typeof(Program).Assembly;

        Assert.That(assembly.GetName().Name, Is.EqualTo("IronGrind.Gateway"));
        Assert.That(assembly.EntryPoint, Is.Not.Null);
    }
}
