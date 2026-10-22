using System;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// <see cref="IZoneApplication"/> over <c>UnityEngine.Application</c> (ADR-009 Amendment 1 Decision 2 S1 and
    /// Decision 3 step 8). Holds no state except the host object it keeps alive. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// IZoneApplication app = new UnityZoneApplication(gameObject);
    /// app.SubscribeWantsToQuit(composition.OnWantsToQuit); // named method group, never a lambda
    /// </code>
    /// </example>
    public sealed class UnityZoneApplication : IZoneApplication
    {
        private readonly GameObject _hostObject;

        /// <summary>Creates the seam.</summary>
        /// <param name="hostObject">The host's GameObject, kept alive by <see cref="KeepAcrossSceneLoads"/>; not null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hostObject"/> is null.</exception>
        public UnityZoneApplication(GameObject hostObject)
        {
            if (hostObject == null)
            {
                throw new ArgumentNullException(nameof(hostObject));
            }

            _hostObject = hostObject;
        }

        /// <inheritdoc/>
        public bool IsEditor => Application.isEditor;

        /// <inheritdoc/>
        public void Quit(int code)
        {
            Application.Quit(code);
        }

        /// <inheritdoc/>
        public void KeepAcrossSceneLoads()
        {
            UnityEngine.Object.DontDestroyOnLoad(_hostObject);
        }

        /// <inheritdoc/>
        public void SubscribeWantsToQuit(Func<bool> handler)
        {
            Application.wantsToQuit += handler;
        }

        /// <inheritdoc/>
        public void UnsubscribeWantsToQuit(Func<bool> handler)
        {
            Application.wantsToQuit -= handler;
        }
    }
}
