#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
namespace IronGrind.Networking
{
    /// <summary>
    /// Test/dev-build-only zone instance configuration and state-readback surface. Allows tests to
    /// inject known entry-point coordinates and capacity limits, and to read back zone/entity state
    /// without polling logs.
    /// </summary>
    /// <remarks>
    /// <para><b>Instance scoping:</b> every mutating method is scoped by <c>zoneInstanceId</c> to
    /// prevent configuration bleed between zone instances running in parallel-instance tests.
    /// <see cref="Reset(uint)"/> in particular must only clear overrides for the given
    /// <c>zoneInstanceId</c> — it must never affect any other zone instance's overrides.</para>
    /// <para><b>Fixed-point encoding:</b> all position parameters/return values use the same
    /// centimeter fixed-point encoding as the wire format (CR-NET-7.2) — multiply by 0.01 to get
    /// world-unit meters.</para>
    /// <para><b>Release-build stripping:</b> see <see cref="ITransportFaultInjector"/> remarks —
    /// the same guard and enforcement rules apply to this interface.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// IZoneTestConfigurator zoneConfig = NetworkingTestHarness.CreateZoneTestConfigurator();
    /// zoneConfig.SetZoneEntryPoint(zoneInstanceId: 7u, posX: 100, posY: 0, posZ: 250);
    /// zoneConfig.SetZoneCapacity(zoneInstanceId: 7u, maxPlayers: 10);
    /// ZoneState state = zoneConfig.GetCurrentZoneState(7u);
    /// zoneConfig.Reset(7u); // clears only zone instance 7's overrides
    /// </code>
    /// </example>
    public interface IZoneTestConfigurator
    {
        /// <summary>
        /// Overrides the zone entry-point coordinates used for ghost-death respawn
        /// (EC-NET-1, AC-NC-33a) for <paramref name="zoneInstanceId"/>.
        /// </summary>
        /// <param name="zoneInstanceId">The zone instance to configure.</param>
        /// <param name="posX">Entry-point X, in centimeters.</param>
        /// <param name="posY">Entry-point Y, in centimeters.</param>
        /// <param name="posZ">Entry-point Z, in centimeters.</param>
        void SetZoneEntryPoint(uint zoneInstanceId, short posX, short posY, short posZ);

        /// <summary>
        /// Overrides the player capacity of <paramref name="zoneInstanceId"/> for overflow tests
        /// (AC-NC-24). Setting to N means the (N+1)th join is rejected with an overflow response.
        /// </summary>
        /// <param name="zoneInstanceId">The zone instance to configure.</param>
        /// <param name="maxPlayers">The overridden player capacity.</param>
        void SetZoneCapacity(uint zoneInstanceId, int maxPlayers);

        /// <summary>Reads the current ST-NET-2 zone state of <paramref name="zoneInstanceId"/> as of the last tick boundary.</summary>
        /// <param name="zoneInstanceId">The zone instance to query.</param>
        ZoneState GetCurrentZoneState(uint zoneInstanceId);

        /// <summary>Reads the current server-side position of <paramref name="entityId"/>, in fixed-point centimeters (AC-NC-33a).</summary>
        /// <param name="entityId">The entity to query.</param>
        (short posX, short posY, short posZ) GetEntityPosition(uint entityId);

        /// <summary>
        /// Overrides <c>LastBeatServerTick</c> for <paramref name="entityId"/> — an injection point
        /// for OWL compensation tests (AC-NC-29, AC-OWL-01–05). Injects a specific tick number as
        /// if the entity's Beat fired at that tick.
        /// </summary>
        /// <param name="entityId">The entity to configure.</param>
        /// <param name="serverTickNumber">The tick number to inject as the entity's last Beat tick.</param>
        void SetLastBeatServerTick(uint entityId, uint serverTickNumber);

        /// <summary>
        /// Overrides the server's estimated one-way-latency (OWL) for <paramref name="entityId"/>'s
        /// client connection, bypassing RTT probe computation (AC-OWL-05).
        /// </summary>
        /// <param name="entityId">The entity whose connection OWL estimate is overridden.</param>
        /// <param name="owlSeconds">The one-way latency to inject, in seconds.</param>
        void SetClientOWL(uint entityId, float owlSeconds);

        /// <summary>
        /// Resets all configuration overrides for <paramref name="zoneInstanceId"/> to zone
        /// template defaults. Scoped only to this zone instance — must not affect any other zone
        /// instance's overrides.
        /// </summary>
        /// <param name="zoneInstanceId">The zone instance whose overrides are reset.</param>
        void Reset(uint zoneInstanceId);
    }
}
#endif
