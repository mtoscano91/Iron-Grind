using System;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Seam over <c>UnityEngine.Application</c> for the zone server composition (ADR-009 Amendment 1 Decision 5).
    /// Story 045 implements it over Unity. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// application.SubscribeWantsToQuit(composition.OnWantsToQuit); // named method group, never a lambda
    /// </code>
    /// </example>
    public interface IZoneApplication
    {
        /// <summary>True when running in the Unity Editor (quit requests are never blocked there).</summary>
        bool IsEditor { get; }

        /// <summary>Ends the process with the given exit code (teardown step 8).</summary>
        /// <param name="code">The <see cref="ZoneExitCode"/> value as an int.</param>
        void Quit(int code);

        /// <summary>Keeps the host object alive across scene loads (<c>DontDestroyOnLoad</c>), startup S1.</summary>
        void KeepAcrossSceneLoads();

        /// <summary>Subscribes a handler to <c>Application.wantsToQuit</c>.</summary>
        /// <param name="handler">A named method; the same delegate value must be passed to unsubscribe.</param>
        void SubscribeWantsToQuit(Func<bool> handler);

        /// <summary>Removes a handler added by <see cref="SubscribeWantsToQuit"/>.</summary>
        /// <param name="handler">The handler to remove.</param>
        void UnsubscribeWantsToQuit(Func<bool> handler);
    }
}
