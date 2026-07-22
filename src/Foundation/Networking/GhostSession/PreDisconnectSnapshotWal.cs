using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// CGS-3's external write-ahead log (WAL) for <see cref="PreDisconnectSnapshot"/> records, keyed
    /// on <c>(characterId, disconnectTickNumber)</c> per CGS-3's own idempotency-key requirement.
    /// Sealed, in-memory <see cref="Dictionary{TKey,TValue}"/> registry, mirroring
    /// <see cref="ConnectionStateMachine"/>'s and <see cref="GhostEntityTracker"/>'s established
    /// per-story registry shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>EC-CGS-2 idempotency — a re-submitted identical snapshot is a no-op, not a second
    /// write:</b> <see cref="TryWriteSnapshot"/> compares the incoming <c>disconnectTickNumber</c>
    /// against whatever is already recorded for <c>characterId</c> (if anything). An identical
    /// resubmission (same <c>characterId</c> AND same <c>disconnectTickNumber</c> — e.g. a retried
    /// call after a simulated crash mid-write, <see cref="IServerCrashInjector.RegisterCrashAt"/>)
    /// leaves the existing entry completely untouched and returns <see langword="false"/>. A
    /// genuinely new ghost period for the same character (a different <c>disconnectTickNumber</c>)
    /// is a fresh write and returns <see langword="true"/>, overwriting the stale prior entry — a
    /// character cannot simultaneously be mid-cleanup for two different ghost periods, so retaining
    /// the old entry would only be stale data.
    /// </para>
    /// <para>
    /// <b>"External WAL" is an in-memory dictionary, not real persistent storage (forward-dependency
    /// scope, same precedent as every prior story's delegate-seam persistence):</b> no real
    /// write-ahead-log or database layer exists yet in this codebase. This class proves the
    /// idempotency-key contract structurally; a future persistence-layer story is expected to back it
    /// with real durable storage (the external-WAL requirement itself — CGS-3 — exists precisely so a
    /// real implementation survives a server crash between promotion and cleanup).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var wal = new PreDisconnectSnapshotWal();
    /// bool wroteFirst = wal.TryWriteSnapshot(characterId: 555u, disconnectTickNumber: 1000u,
    ///     new PreDisconnectSnapshot(hp: 42, respawnPosition: (0, 0, 0))); // true
    ///
    /// // A re-submitted identical snapshot (e.g. after a simulated crash mid-write) is a no-op:
    /// bool wroteRetry = wal.TryWriteSnapshot(characterId: 555u, disconnectTickNumber: 1000u,
    ///     new PreDisconnectSnapshot(hp: 42, respawnPosition: (0, 0, 0))); // false — untouched
    ///
    /// bool found = wal.TryGetSnapshot(characterId: 555u, out PreDisconnectSnapshot snapshot); // true, hp == 42
    /// </code>
    /// </example>
    public sealed class PreDisconnectSnapshotWal
    {
        private readonly Dictionary<uint, (uint disconnectTickNumber, PreDisconnectSnapshot snapshot)> _entries = new();

        /// <summary>
        /// Writes <paramref name="snapshot"/> for <paramref name="characterId"/>, keyed on
        /// <paramref name="disconnectTickNumber"/> (CGS-3). Idempotent per EC-CGS-2 — see class
        /// remarks.
        /// </summary>
        /// <param name="characterId">The character the snapshot belongs to.</param>
        /// <param name="disconnectTickNumber">
        /// The server tick the character disconnected at — the idempotency key, alongside
        /// <paramref name="characterId"/>.
        /// </param>
        /// <param name="snapshot">The pre-disconnect snapshot to write.</param>
        /// <returns>
        /// <see langword="true"/> if a new entry was written (no prior entry existed, or the prior
        /// entry had a different <paramref name="disconnectTickNumber"/>); <see langword="false"/> if
        /// an identical entry already existed and this call was a no-op (EC-CGS-2).
        /// </returns>
        /// <example>
        /// <code>
        /// bool wrote = wal.TryWriteSnapshot(characterId: 555u, disconnectTickNumber: 1000u,
        ///     new PreDisconnectSnapshot(hp: 42, respawnPosition: (0, 0, 0)));
        /// </code>
        /// </example>
        public bool TryWriteSnapshot(uint characterId, uint disconnectTickNumber, PreDisconnectSnapshot snapshot)
        {
            if (_entries.TryGetValue(characterId, out (uint disconnectTickNumber, PreDisconnectSnapshot snapshot) existing)
                && existing.disconnectTickNumber == disconnectTickNumber)
            {
                return false; // EC-CGS-2: identical re-submission is a no-op — entry left untouched.
            }

            _entries[characterId] = (disconnectTickNumber, snapshot);
            return true;
        }

        /// <summary>
        /// Returns the most recently written <see cref="PreDisconnectSnapshot"/> for
        /// <paramref name="characterId"/>, if one exists.
        /// </summary>
        /// <param name="characterId">The character to query.</param>
        /// <param name="snapshot">The recorded snapshot, if found; otherwise <see langword="default"/>.</param>
        /// <returns><see langword="true"/> if <paramref name="characterId"/> has a recorded snapshot.</returns>
        /// <example>
        /// <code>bool found = wal.TryGetSnapshot(characterId: 555u, out PreDisconnectSnapshot snapshot);</code>
        /// </example>
        public bool TryGetSnapshot(uint characterId, out PreDisconnectSnapshot snapshot)
        {
            if (_entries.TryGetValue(characterId, out (uint disconnectTickNumber, PreDisconnectSnapshot snapshot) entry))
            {
                snapshot = entry.snapshot;
                return true;
            }

            snapshot = default;
            return false;
        }
    }
}
