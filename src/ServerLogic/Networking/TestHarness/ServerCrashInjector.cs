#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// In-memory implementation of <see cref="IServerCrashInjector"/>. Stores at most one
    /// registered <see cref="CrashStep"/> and synchronously invokes a caller-supplied crash
    /// callback when <see cref="SignalStepReached"/> reports a matching step.
    /// </summary>
    /// <remarks>
    /// There is no real persistence/crash-recovery layer yet (deferred to Stories 011, 015,
    /// 018–021), so this class models "the persistence write" as a caller-supplied delegate: the
    /// caller (a test fixture today; the real persistence write path in a future story) calls
    /// <see cref="SignalStepReached"/> at the exact point the GDD names for each
    /// <see cref="CrashStep"/>, passing the crash callback to invoke if that step is registered.
    /// The invocation is always synchronous, in the caller's own call stack — never deferred via
    /// <c>Task</c> or <c>Invoke</c>, which would race the transmission step this interface exists
    /// to test.
    /// </remarks>
    public sealed class ServerCrashInjector : IServerCrashInjector
    {
        private CrashStep? _registeredStep;

        /// <inheritdoc/>
        public void RegisterCrashAt(CrashStep step)
        {
            _registeredStep = step;
        }

        /// <inheritdoc/>
        public void ClearRegistered()
        {
            _registeredStep = null;
        }

        // ---------------------------------------------------------------------
        // Internal seam: called by the caller at the exact point a crash-eligible
        // step is reached. Visible to IronGrind.Foundation.EditModeTests via
        // InternalsVisibleTo (src/Foundation/AssemblyInfo.cs).
        // ---------------------------------------------------------------------

        /// <summary>
        /// Signals that <paramref name="step"/> has just been reached. If <paramref name="step"/>
        /// matches the currently registered <see cref="CrashStep"/>, <paramref name="onCrash"/> is
        /// invoked synchronously, in this call stack, before this method returns — satisfying the
        /// "crash fires before any message is queued for transmission" invariant (AC-TH-2). Does
        /// nothing if no step is registered or the registered step does not match.
        /// </summary>
        /// <param name="step">The sequence step the caller has just reached.</param>
        /// <param name="onCrash">
        /// The crash callback to invoke synchronously if <paramref name="step"/> matches the
        /// registered step. May be <see langword="null"/> — treated as a no-op crash.
        /// </param>
        internal void SignalStepReached(CrashStep step, Action onCrash)
        {
            if (_registeredStep.HasValue && _registeredStep.Value == step)
            {
                onCrash?.Invoke();
            }
        }
    }
}
#endif
