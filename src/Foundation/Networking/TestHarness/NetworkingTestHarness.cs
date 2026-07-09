#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
namespace IronGrind.Networking
{
    /// <summary>
    /// Test/dev-build-only wiring point for the fault/crash/config injection interfaces.
    /// </summary>
    /// <remarks>
    /// <para>This project has no central DI container — ADR-010 (Event/Messaging Architecture)
    /// explicitly forbids a service-locator/EventBus singleton in favor of direct constructor
    /// injection (Tier 1). These factory methods are the guarded "registration call site" that
    /// <c>networking-test-harness.md</c>'s Release-Build Stripping enforcement rule 3 requires:
    /// callers construct concrete instances here rather than resolving them from a container. Like
    /// the interfaces and implementations above, this entire class compiles only inside the
    /// test/dev-build guard, so it disappears from release builds along with everything else on
    /// this file's compile-guard.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ITransportFaultInjector faultInjector = NetworkingTestHarness.CreateTransportFaultInjector();
    /// IServerCrashInjector crashInjector = NetworkingTestHarness.CreateServerCrashInjector();
    /// IZoneTestConfigurator zoneConfig = NetworkingTestHarness.CreateZoneTestConfigurator();
    /// INetworkTestObserver observer = NetworkingTestHarness.CreateNetworkTestObserver();
    /// </code>
    /// </example>
    public static class NetworkingTestHarness
    {
        /// <summary>Creates a new <see cref="ITransportFaultInjector"/> instance for a single test/session.</summary>
        public static ITransportFaultInjector CreateTransportFaultInjector() => new TransportFaultInjector();

        /// <summary>Creates a new <see cref="IServerCrashInjector"/> instance for a single test/session.</summary>
        public static IServerCrashInjector CreateServerCrashInjector() => new ServerCrashInjector();

        /// <summary>Creates a new <see cref="IZoneTestConfigurator"/> instance for a single test/session.</summary>
        public static IZoneTestConfigurator CreateZoneTestConfigurator() => new ZoneTestConfigurator();

        /// <summary>Creates a new <see cref="INetworkTestObserver"/> instance for a single test/session.</summary>
        public static INetworkTestObserver CreateNetworkTestObserver() => new NetworkTestObserver();
    }
}
#endif
