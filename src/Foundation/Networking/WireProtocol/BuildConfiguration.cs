namespace IronGrind.Networking
{
    /// <summary>
    /// Establishes this project's first debug-build/release-build distinction (Story 025, EC-MCR-1 /
    /// AC-MCR-03). Every existing test-only hook in this codebase already gates on
    /// <c>#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD</c> (see <see cref="INetworkTestObserver"/>'s
    /// own remarks) — that pattern answers "is this a test/dev context," not "is this a debug
    /// player build." <c>DEVELOPMENT_BUILD</c> is Unity's actual player debug-build symbol (set by
    /// the "Development Build" checkbox in Build Settings); plain <c>#if DEBUG</c> is not a reliable
    /// Unity player-build symbol (Unity does not define <c>DEBUG</c> for IL2CPP Release player
    /// builds the way a plain .NET project would). This class is therefore the project's chosen
    /// mapping: <b>debug build</b> = <c>DEVELOPMENT_BUILD</c> defined; <b>release build</b> =
    /// <c>DEVELOPMENT_BUILD</c> undefined. Future stories needing the same distinction should reuse
    /// <see cref="IsDevelopmentBuild"/> rather than re-deriving this mapping.
    /// </summary>
    /// <remarks>
    /// <see cref="MessageRoutingRegistry.ValidateAndRoute"/> takes its debug/release decision as an
    /// explicit <see langword="bool"/> parameter rather than branching on this property internally —
    /// per coding-standards.md's "dependency injection over singletons" rule, and because Unity
    /// EditMode tests do not run with <c>DEVELOPMENT_BUILD</c> defined (only <c>UNITY_INCLUDE_TESTS</c>
    /// is), so a test needs to force both the debug and release code paths deterministically. Real
    /// call sites pass <see cref="IsDevelopmentBuild"/>; tests pass literal <see langword="true"/>/
    /// <see langword="false"/>.
    /// </remarks>
    public static class BuildConfiguration
    {
        /// <summary>
        /// <see langword="true"/> when compiled with <c>DEVELOPMENT_BUILD</c> defined (Unity's actual
        /// debug-build symbol) — see class remarks for why this symbol was chosen over <c>DEBUG</c>.
        /// </summary>
        public static bool IsDevelopmentBuild =>
#if DEVELOPMENT_BUILD
            true;
#else
            false;
#endif
    }
}
