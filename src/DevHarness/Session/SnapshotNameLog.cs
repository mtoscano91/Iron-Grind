using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.Networking;

namespace IronGrind.DevHarness.Session
{
    /// <summary>
    /// Unity-free helper that turns a reassembled zone snapshot payload into log lines (Authentication Story 017 evidence):
    /// one line per player name and a summary. Dev harness only.
    /// </summary>
    public static class SnapshotNameLog
    {
        /// <summary>
        /// Walks the packed <see cref="EntityState"/> entries of <paramref name="snapshot"/> and appends
        /// <c>snapshot player name=&lt;name&gt;</c> per player, then <c>snapshot entities=&lt;count&gt; players=&lt;count&gt;</c>.
        /// If an entry fails to decode, appends <c>snapshot decode failed at offset=&lt;n&gt;</c> and stops (no summary).
        /// </summary>
        /// <param name="snapshot">The reassembled payload (exact length, not the whole buffer).</param>
        /// <param name="output">Receives the lines; not cleared.</param>
        /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
        public static void FormatPlayerNames(ReadOnlySpan<byte> snapshot, List<string> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            int offset = 0;
            int entities = 0;
            int players = 0;
            while (offset < snapshot.Length)
            {
                if (!EntityStateCodec.TryReadBody(snapshot.Slice(offset), out EntityState state, out int consumed) || consumed <= 0)
                {
                    output.Add("snapshot decode failed at offset=" + offset.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                entities++;
                if (state.Type == EntityType.Player)
                {
                    players++;
                    output.Add("snapshot player name=" + state.CharacterName);
                }

                offset += consumed;
            }

            output.Add("snapshot entities=" + entities.ToString(CultureInfo.InvariantCulture)
                + " players=" + players.ToString(CultureInfo.InvariantCulture));
        }
    }
}
