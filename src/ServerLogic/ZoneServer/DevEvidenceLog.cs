using System;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Dev-only evidence log lines for manual end-to-end runs (Authentication Story 017: <c>Admit</c> and
    /// <c>LoadCharacter</c> times). Off unless <see cref="Enabled"/> is set, which only <see cref="ZoneServerHost"/> does,
    /// in the Editor and development builds, so EditMode tests that assert on logs never see these lines. Each line
    /// carries a UTC timestamp so it can be ordered against the client's log. Never pass a token or password.
    /// </summary>
    internal static class DevEvidenceLog
    {
        /// <summary>True when evidence lines are written. Main thread only.</summary>
        internal static bool Enabled;

        /// <summary>Writes <c>prefix timestamp text</c> when <see cref="Enabled"/>.</summary>
        /// <param name="prefix">The calling class's log prefix.</param>
        /// <param name="text">The evidence text.</param>
        internal static void Write(string prefix, string text)
        {
            if (!Enabled)
            {
                return;
            }

            Debug.Log(prefix + " " + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) + " " + text);
        }
    }
}
