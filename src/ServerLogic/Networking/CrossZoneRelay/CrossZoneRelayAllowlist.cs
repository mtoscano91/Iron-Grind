using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>One allowlist row: a message type and the longest body the relay accepts for it.</summary>
    public readonly struct CrossZoneRelayAllowlistRow
    {
        /// <summary>The wire message type.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>The longest body, in bytes, the relay carries for the type.</summary>
        public readonly int MaxBodyBytes;

        /// <summary>Creates a row.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="maxBodyBytes">The longest body in bytes; at least 0.</param>
        public CrossZoneRelayAllowlistRow(ushort messageTypeId, int maxBodyBytes)
        {
            MessageTypeId = messageTypeId;
            MaxBodyBytes = maxBodyBytes;
        }
    }

    /// <summary>
    /// The relay allowlist (ADR-015 Decision 4): <c>MessageTypeId</c> to <c>MaxBodyBytes</c>. Built by the caller
    /// (the composition root) and sealed at construction: it copies the rows and has no mutating member. Both the
    /// broadcaster (send) and the receiver (receive) check it. A row is added only by a story that names the GDD
    /// rule requiring cross-zone delivery.
    /// </summary>
    /// <remarks>Immutable, so safe to read from the listener thread and the tick thread at once.</remarks>
    /// <example>
    /// <code>
    /// CrossZoneRelayAllowlist allowlist = CrossZoneRelayAllowlist.CreateProduction();
    /// bool ok = allowlist.TryGetMaxBodyBytes(ServerBroadcast_Enhancement9.MessageTypeId, out int max);   // true, 52
    /// </code>
    /// </example>
    public sealed class CrossZoneRelayAllowlist
    {
        private readonly Dictionary<ushort, int> _maxBodyByType;

        /// <summary>Creates the allowlist from the given rows; a repeated type keeps its last row.</summary>
        /// <param name="rows">The rows; not null, at least one.</param>
        /// <exception cref="ArgumentNullException"><paramref name="rows"/> is null.</exception>
        /// <exception cref="ArgumentException">There is no row, or a row has a negative body size.</exception>
        public CrossZoneRelayAllowlist(IEnumerable<CrossZoneRelayAllowlistRow> rows)
        {
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            _maxBodyByType = new Dictionary<ushort, int>();
            foreach (CrossZoneRelayAllowlistRow row in rows)
            {
                if (row.MaxBodyBytes < 0)
                {
                    throw new ArgumentException("MaxBodyBytes must not be negative.", nameof(rows));
                }

                _maxBodyByType[row.MessageTypeId] = row.MaxBodyBytes;
            }

            if (_maxBodyByType.Count == 0)
            {
                throw new ArgumentException("The allowlist needs at least one row.", nameof(rows));
            }

            int largest = 0;
            foreach (KeyValuePair<ushort, int> pair in _maxBodyByType)
            {
                if (pair.Value > largest)
                {
                    largest = pair.Value;
                }
            }

            LargestMaxBodyBytes = largest;
        }

        /// <summary>Number of rows.</summary>
        public int Count => _maxBodyByType.Count;

        /// <summary>The largest <c>MaxBodyBytes</c> of any row (the inbox slot size).</summary>
        public int LargestMaxBodyBytes { get; }

        /// <summary>The production allowlist: one row, <c>ServerBroadcast_Enhancement9</c> to 52 bytes.</summary>
        /// <returns>A new allowlist.</returns>
        public static CrossZoneRelayAllowlist CreateProduction()
        {
            return new CrossZoneRelayAllowlist(new[]
            {
                new CrossZoneRelayAllowlistRow(ServerBroadcast_Enhancement9.MessageTypeId, ServerBroadcast_Enhancement9.MaxBodySize),
            });
        }

        /// <summary>Looks up a type. Allocates nothing.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="maxBodyBytes">The longest body for the type, or 0 when it is not listed.</param>
        /// <returns>True when the type is on the allowlist.</returns>
        public bool TryGetMaxBodyBytes(ushort messageTypeId, out int maxBodyBytes)
        {
            return _maxBodyByType.TryGetValue(messageTypeId, out maxBodyBytes);
        }
    }
}
