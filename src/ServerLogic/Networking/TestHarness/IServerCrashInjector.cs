#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
namespace IronGrind.Networking
{
    /// <summary>
    /// Test/dev-build-only crash injection surface. Registers a single crash trigger at a named
    /// step of a critical persistence sequence, used to verify durability and duplicate-rejection
    /// behavior when the server is killed at that exact point (AC-NC-16, AC-NC-34).
    /// </summary>
    /// <remarks>
    /// <para><b>Synchronous crash invariant:</b> when <see cref="CrashStep.AfterPersistenceWrite"/>
    /// is registered, the concrete implementation's crash callback must fire synchronously — within
    /// the same call stack that completes the (simulated) persistence write — before any message is
    /// queued for transmission. Implementations must never use a deferred/async pattern (no
    /// <c>Task</c>, no <c>Invoke</c>) to trigger the crash; a deferred crash would introduce a race
    /// with the transmission step these tests are designed to catch.</para>
    /// <para><b>Release-build stripping:</b> see <see cref="ITransportFaultInjector"/> remarks —
    /// the same guard and enforcement rules apply to this interface.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// IServerCrashInjector crashInjector = NetworkingTestHarness.CreateServerCrashInjector();
    /// crashInjector.RegisterCrashAt(CrashStep.AfterPersistenceWrite);
    /// // ... drive the code path that performs the persistence write and signals the step ...
    /// crashInjector.ClearRegistered();
    /// </code>
    /// </example>
    public interface IServerCrashInjector
    {
        /// <summary>
        /// Registers a crash to trigger at <paramref name="step"/> of a critical persistence
        /// sequence. Covers the CR-NET-5 enhancement sequence, the CR-NET-6.5 TTL-expiry sequence,
        /// and the CR-GH-10 ghost-cleanup sequence. Only one step may be registered at a time — a
        /// second call replaces any previously registered step.
        /// </summary>
        /// <param name="step">The sequence step at which the crash should fire.</param>
        void RegisterCrashAt(CrashStep step);

        /// <summary>Clears any registered crash injection. Subsequent steps reached fire no crash.</summary>
        void ClearRegistered();
    }

    /// <summary>
    /// Named steps within the three critical commit-before-broadcast/cleanup sequences that
    /// <see cref="IServerCrashInjector"/> can interrupt. Test-only — never referenced by production
    /// code outside the <c>UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> guard.
    /// </summary>
    public enum CrashStep : byte
    {
        /// <summary>CR-NET-5 enhancement sequence: outcome committed to persistence, before broadcast.</summary>
        AfterPersistenceWrite = 0,

        /// <summary>CR-NET-5 enhancement sequence: outcome queued for transmission, before transport confirms.</summary>
        AfterOutcomeEmit = 1,

        /// <summary>CR-NET-5.6 respec commit-before-broadcast.</summary>
        AfterPersistenceWriteRespec = 2,

        /// <summary>CR-NET-6.5 session-TTL-expiry sequence: character state committed (step 3), before <c>PlayerLeftZone</c> (step 4).</summary>
        AfterTTLExpiryPersistenceWrite = 3,

        /// <summary>CR-GH-10 ghost-cleanup sequence: ghost write committed, before session slot release.</summary>
        AfterGhostCleanupPersistenceWrite = 4,
    }
}
#endif
