using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Networking Core Story 022: the CR-OWL-1 <c>LastBeatServerTick uint[]</c> data structure and
    /// the CR-OWL-4 entity slot allocation contract (player join/leave/ghost-preserve lifecycle,
    /// mob spawn/despawn free-list lifecycle). A sealed, per-zone-instance stateful class — one
    /// instance per zone, constructed fresh per zone (same shape/precedent as
    /// <see cref="ZoneSessionStateMachine"/> and <see cref="GhostEntityTracker"/>), never a static
    /// singleton.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Array size is fixed to the two GDD-registry constants directly, not a caller-supplied
    /// capacity</b> (unlike <see cref="ZoneBufferPool"/>'s optional <c>capacity</c> constructor
    /// parameter): CR-OWL-4's "Array size" clause pins the formula to
    /// <c><see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/> + <see cref="MAX_MOBS_PER_ZONE"/></c>
    /// specifically, so this class's constructor takes no parameters at all. Every entry is
    /// initialized to <see cref="uint.MaxValue"/> (the sentinel — "never fired a Beat").
    /// </para>
    /// <para>
    /// <b>Player region <c>[0, MAX_PLAYERS_PER_ZONE)</c>, mob region
    /// <c>[MAX_PLAYERS_PER_ZONE, TotalSlotCount)</c></b> — this partition is implied by CR-OWL-4's
    /// array-size formula plus the fact that players and mobs have entirely separate allocation
    /// lifecycles (players: join/leave/TTL-expiry, dictionary-keyed by <c>playerEntityId</c>; mobs:
    /// spawn/despawn, free-list-based, no entity-id key at all since no Zone Instancing system
    /// exists yet to hand this class a mob entity id).
    /// </para>
    /// <para>
    /// <b><see cref="MAX_MOBS_PER_ZONE"/> is a placeholder pending the not-yet-authored Zone
    /// Instancing GDD</b> — same forward-dependency-placeholder treatment as
    /// <see cref="ZoneSnapshotReassemblyTracker.MAX_SNAPSHOT_RETRANSMIT_ATTEMPTS"/>'s own precedent
    /// (Story 015): the GDD's Tuning Knobs table pins the default (150) and safe range ([50, 500])
    /// today, but the authoritative value is Zone Instancing's to confirm (CR-ZI-8 step 10 per the
    /// GDD's Dependencies section).
    /// </para>
    /// <para>
    /// <b><see cref="MAX_WRAP_WINDOW_TICKS"/>'s upper bound is documented, not enforced, here</b> —
    /// CR-OWL-3 derives <c>MAX_WRAP_WINDOW_TICKS ≤ floor(MAX_COMPENSATABLE_OWL_MS / (1000 /
    /// TICK_RATE_HZ)) = floor(120 / 50) = 2</c>. Re-derive this bound whenever
    /// <c>MAX_COMPENSATABLE_OWL_MS</c> or <c>TICK_RATE_HZ</c> change. Asserting this bound in CI is
    /// AC-OWL-07's job (a pipeline concern), explicitly out of scope for this class.
    /// </para>
    /// <para>
    /// <b>Deliberately does not route through <see cref="StaleDiscardComparer"/></b>: CR-OWL-1's
    /// <c>wrapCorrectionWindow</c> check is a small-window recency check (<c>≤ 2</c> ticks), not an
    /// RFC 1982 "is this candidate newer across the full uint range" comparison —
    /// <see cref="StaleDiscardComparer.IsTickExpired"/>'s half-circle semantics would be the wrong
    /// tool here and would also hide the exact sentinel-guard-then-subtract short-circuit order
    /// CR-OWL-1/EC-OWL-3 require. <see cref="IsWithinWrapCorrectionWindow"/> implements the GDD's
    /// literal formula directly.
    /// </para>
    /// <para>
    /// <b>Honest AC-terminology scoping (read before calling <see cref="IsWithinWrapCorrectionWindow"/>):</b>
    /// this method implements only the CR-OWL-1 <c>wrapCorrectionWindow</c> sub-check, not the full
    /// F-OWL-1 <c>wrapCorrectionActive</c> formula (which additionally requires
    /// <c>signedAdjusted &lt; 0</c>). The story's own AC-OWL-03/AC-OWL-04 acceptance-criteria text
    /// names <c>wrapCorrectionActive</c>, but both ACs hold <c>signedAdjusted &lt; 0</c> as a
    /// given/constant precondition — so what they actually exercise is this window sub-check in
    /// isolation. See the method's own doc comment for the full explanation. Composing this with
    /// <c>signedAdjusted</c>/<c>adjustedCycleTimer</c> into the real F-OWL-1 formula is Story 023's
    /// job — this class does not implement or reference either of those two values anywhere.
    /// </para>
    /// <para>
    /// <b>Out of scope, owned by neighbouring stories:</b> wiring <see cref="RecordBeat"/> into the
    /// tick loop's Beat-evaluation phase (Story 009, already Complete — a later story extends it;
    /// <c>ServerTickLoop.cs</c> is untouched by this story). Real mob spawn/despawn slot allocation
    /// callers (future Zone Instancing epic — <see cref="AllocateMobSlot"/>/<see cref="DeallocateMobSlot"/>
    /// are the mock-provider-testable contract only). <see cref="IZoneTestConfigurator.SetLastBeatServerTick"/>
    /// wiring (Story 001's pre-built test seam for a future story — untouched here).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var tracker = new LastBeatServerTickTracker();
    ///
    /// // Player joins the zone (PlayerJoinedZone processing, CR-OWL-4):
    /// int playerSlot = tracker.AllocatePlayerSlot(playerEntityId: 555u);
    ///
    /// // Story 009's tick loop Beat-evaluation phase fires (a later story wires this call in):
    /// tracker.RecordBeat(playerSlot, serverTickNumber: 400u);
    ///
    /// // CR-OWL-1's wrapCorrectionWindow sub-check, one tick later:
    /// bool withinWindow = tracker.IsWithinWrapCorrectionWindow(playerSlot, serverTickNumber: 401u,
    ///     maxWrapWindowTicks: LastBeatServerTickTracker.MAX_WRAP_WINDOW_TICKS); // true (401-400=1&lt;=2)
    ///
    /// // Ghost period: player disconnects/reconnects — slot is NOT deallocated (EC-OWL-2).
    /// // ... only real PlayerLeftZone / TTL expiry calls this:
    /// tracker.DeallocatePlayerSlot(playerEntityId: 555u);
    ///
    /// // Mob spawns, despawns, and its slot is recycled (CR-OWL-4 sanitization invariant):
    /// int mobSlot = tracker.AllocateMobSlot();
    /// tracker.RecordBeat(mobSlot, serverTickNumber: 900u);
    /// tracker.DeallocateMobSlot(mobSlot); // resets LastBeatServerTick[mobSlot] to uint.MaxValue first
    /// int reallocatedSlot = tracker.AllocateMobSlot(); // may be the same index; sentinel-clean
    /// </code>
    /// </example>
    public sealed class LastBeatServerTickTracker
    {
        /// <summary>
        /// Maximum concurrent mobs in a single zone instance (GDD Tuning Knobs default; safe range
        /// [50, 500]) — a placeholder pending the not-yet-authored Zone Instancing GDD (see class
        /// remarks). Determines the mob region size of the backing array alongside
        /// <see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/>.
        /// </summary>
        public const int MAX_MOBS_PER_ZONE = 150;

        /// <summary>
        /// GDD default tuning knob: the number of ticks after a Beat during which
        /// <see cref="IsWithinWrapCorrectionWindow"/>'s window sub-check can return
        /// <see langword="true"/>. Upper-bounded by
        /// <c>floor(MAX_COMPENSATABLE_OWL_MS / (1000 / TICK_RATE_HZ)) = floor(120 / 50) = 2</c>
        /// per CR-OWL-3 — this class documents that derivation but does not enforce the bound
        /// itself (AC-OWL-07 is a CI-pipeline concern, out of scope here).
        /// </summary>
        public const uint MAX_WRAP_WINDOW_TICKS = 2;

        /// <summary>
        /// Tracks whether a mob-region slot is currently in use, returned to the free list, or has
        /// never been allocated at all. Default (<c>0</c>) is <see cref="NeverAllocated"/> so a
        /// freshly-constructed <see cref="_mobSlotState"/> array needs no explicit initialization
        /// loop.
        /// </summary>
        private enum MobSlotState
        {
            NeverAllocated = 0,
            InUse,
            Free,
        }

        private readonly uint[] _lastBeatServerTick;

        private readonly Dictionary<uint, int> _playerEntityIdToSlot = new();
        private readonly bool[] _playerSlotOccupied;

        private readonly MobSlotState[] _mobSlotState;
        private readonly Queue<int> _freeMobSlots = new();
        private int _nextUnusedMobIndex;

        /// <summary>
        /// The total number of slots in this zone instance's <c>LastBeatServerTick</c> array
        /// (<c><see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/> + <see cref="MAX_MOBS_PER_ZONE"/></c>,
        /// CR-OWL-4 "Array size").
        /// </summary>
        public int TotalSlotCount => _lastBeatServerTick.Length;

        /// <summary>
        /// Allocates the backing <c>LastBeatServerTick uint[]</c> at
        /// <c><see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/> + <see cref="MAX_MOBS_PER_ZONE"/></c>
        /// entries, every entry initialized to <see cref="uint.MaxValue"/> (CR-OWL-1, CR-OWL-4
        /// "Array size"). Models "initialized at zone creation" — call once per zone instance.
        /// </summary>
        /// <example>
        /// <code>var tracker = new LastBeatServerTickTracker();</code>
        /// </example>
        public LastBeatServerTickTracker()
        {
            _lastBeatServerTick = new uint[ZoneBufferPool.MAX_PLAYERS_PER_ZONE + MAX_MOBS_PER_ZONE];
            for (int i = 0; i < _lastBeatServerTick.Length; i++)
            {
                _lastBeatServerTick[i] = uint.MaxValue;
            }

            _playerSlotOccupied = new bool[ZoneBufferPool.MAX_PLAYERS_PER_ZONE];
            _mobSlotState = new MobSlotState[MAX_MOBS_PER_ZONE];
        }

        /// <summary>
        /// Records a Beat firing for <paramref name="slot"/> (CR-OWL-1: <c>LastBeatServerTick[slot]
        /// = ServerTickNumber</c>), updated atomically at Beat-evaluation time. This is the method a
        /// future story wires into Story 009's tick-driven Beat-evaluation phase (already Complete;
        /// this story does not touch <c>ServerTickLoop.cs</c> — see class remarks).
        /// </summary>
        /// <param name="slot">The entity's slot index, previously returned by
        /// <see cref="AllocatePlayerSlot"/> or <see cref="AllocateMobSlot"/>.</param>
        /// <param name="serverTickNumber">The server's monotonically increasing tick counter at the
        /// moment this entity's Beat fired.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside
        /// <c>[0, <see cref="TotalSlotCount"/>)</c>.</exception>
        /// <example>
        /// <code>tracker.RecordBeat(slot: 5, serverTickNumber: 400u);</code>
        /// </example>
        public void RecordBeat(int slot, uint serverTickNumber)
        {
            ValidateSlot(slot, nameof(slot), nameof(RecordBeat));
            _lastBeatServerTick[slot] = serverTickNumber;
        }

        /// <summary>
        /// Reads back <paramref name="slot"/>'s current <c>LastBeatServerTick</c> value — including
        /// the <see cref="uint.MaxValue"/> sentinel, if the slot has never recorded a Beat. Does not
        /// interpret the value; that is <see cref="IsWithinWrapCorrectionWindow"/>'s and the
        /// caller's job.
        /// </summary>
        /// <param name="slot">The entity's slot index.</param>
        /// <param name="serverTickNumber">The slot's current <c>LastBeatServerTick</c> value.</param>
        /// <returns><see langword="true"/> for any valid slot index (this method always populates
        /// <paramref name="serverTickNumber"/> for a valid slot — see <see cref="ArgumentOutOfRangeException"/>
        /// below for the only failure mode).</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside
        /// <c>[0, <see cref="TotalSlotCount"/>)</c>.</exception>
        /// <example>
        /// <code>
        /// if (tracker.TryGetLastBeatTick(slot: 5, out uint lastBeatTick))
        /// {
        ///     // lastBeatTick may legitimately be uint.MaxValue (never fired a Beat)
        /// }
        /// </code>
        /// </example>
        public bool TryGetLastBeatTick(int slot, out uint serverTickNumber)
        {
            ValidateSlot(slot, nameof(slot), nameof(TryGetLastBeatTick));
            serverTickNumber = _lastBeatServerTick[slot];
            return true;
        }

        /// <summary>
        /// CR-OWL-1's <c>wrapCorrectionWindow</c> sub-check only:
        /// <c>(LastBeatServerTick[slot] != uint.MaxValue) AND ((serverTickNumber -
        /// LastBeatServerTick[slot]) &lt;= maxWrapWindowTicks)</c>. The sentinel guard is evaluated
        /// first via C# <c>&amp;&amp;</c> short-circuit — this is the exact mechanism that prevents
        /// the uint-underflow false positive at <paramref name="serverTickNumber"/> = 0 or 1
        /// (EC-OWL-3): without it, <c>0 - uint.MaxValue = 1 &lt;= 2</c> would evaluate to
        /// <see langword="true"/> for every never-fired slot. The subtraction is unsigned arithmetic
        /// — never cast to <see langword="int"/>.
        /// </summary>
        /// <remarks>
        /// <b>Honest scoping — read this before treating this method's return value as
        /// "wrapCorrectionActive":</b> this is <i>not</i> the full F-OWL-1 <c>wrapCorrectionActive</c>
        /// formula, which is <c>(signedAdjusted &lt; 0) AND (this method's result)</c>. This class
        /// implements only the second conjunct. The story's own AC-OWL-03/AC-OWL-04 acceptance-
        /// criteria text uses the term "wrapCorrectionActive," but both ACs hold
        /// <c>signedAdjusted &lt; 0</c> as a given/constant precondition rather than exercising it —
        /// so what they actually exercise, and what this method actually implements, is this window
        /// sub-check in isolation. Composing <c>signedAdjusted</c>/<c>adjustedCycleTimer</c> into the
        /// real F-OWL-1 formula is Story 023's job; this class does not implement or reference
        /// either value anywhere.
        /// </remarks>
        /// <param name="slot">The entity's slot index.</param>
        /// <param name="serverTickNumber">The server's current tick number.</param>
        /// <param name="maxWrapWindowTicks">The wrap-correction window width, in ticks (GDD default
        /// <see cref="MAX_WRAP_WINDOW_TICKS"/>).</param>
        /// <returns><see langword="true"/> if <paramref name="slot"/> has a recorded Beat within
        /// <paramref name="maxWrapWindowTicks"/> of <paramref name="serverTickNumber"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside
        /// <c>[0, <see cref="TotalSlotCount"/>)</c>.</exception>
        /// <example>
        /// <code>
        /// tracker.RecordBeat(slot: 5, serverTickNumber: 400u);
        /// tracker.IsWithinWrapCorrectionWindow(slot: 5, serverTickNumber: 402u, maxWrapWindowTicks: 2); // true (402-400=2&lt;=2)
        /// tracker.IsWithinWrapCorrectionWindow(slot: 5, serverTickNumber: 403u, maxWrapWindowTicks: 2); // false (403-400=3&gt;2)
        /// </code>
        /// </example>
        public bool IsWithinWrapCorrectionWindow(int slot, uint serverTickNumber, uint maxWrapWindowTicks)
        {
            ValidateSlot(slot, nameof(slot), nameof(IsWithinWrapCorrectionWindow));

            uint lastBeatTick = _lastBeatServerTick[slot];
            return lastBeatTick != uint.MaxValue && (serverTickNumber - lastBeatTick) <= maxWrapWindowTicks;
        }

        /// <summary>
        /// Allocates a player slot in <c>[0, <see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/>)</c>
        /// for <paramref name="playerEntityId"/> (CR-OWL-4: "Slot allocated at
        /// <c>PlayerJoinedZone</c> processing"). The slot is preserved through the ghost period
        /// (<c>Disconnected_SessionActive → Reconnecting → Connected</c>, EC-OWL-2) — this class has
        /// no method that resets or reassigns it during that window; only <see cref="DeallocatePlayerSlot"/>
        /// releases it, and nothing in this class calls that on its own.
        /// </summary>
        /// <param name="playerEntityId">The joining player's entity id.</param>
        /// <returns>The allocated slot index.</returns>
        /// <exception cref="InvalidOperationException"><paramref name="playerEntityId"/> already has
        /// an allocated slot (defensive double-allocation guard, matching
        /// <see cref="GhostEntityTracker.PromoteToGhost"/>'s own double-promotion guard precedent);
        /// or all <see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/> player slots are already
        /// occupied.</exception>
        /// <example>
        /// <code>int slot = tracker.AllocatePlayerSlot(playerEntityId: 555u);</code>
        /// </example>
        public int AllocatePlayerSlot(uint playerEntityId)
        {
            if (_playerEntityIdToSlot.ContainsKey(playerEntityId))
            {
                throw new InvalidOperationException(
                    $"[LastBeatServerTickTracker] {nameof(AllocatePlayerSlot)}: playerEntityId={playerEntityId} " +
                    "already has an allocated slot.");
            }

            for (int i = 0; i < _playerSlotOccupied.Length; i++)
            {
                if (!_playerSlotOccupied[i])
                {
                    _playerSlotOccupied[i] = true;
                    _playerEntityIdToSlot[playerEntityId] = i;
                    return i;
                }
            }

            throw new InvalidOperationException(
                $"[LastBeatServerTickTracker] {nameof(AllocatePlayerSlot)}: all " +
                $"{ZoneBufferPool.MAX_PLAYERS_PER_ZONE} player slots are already occupied.");
        }

        /// <summary>
        /// Deallocates <paramref name="playerEntityId"/>'s player slot (CR-OWL-4: "Slot deallocated
        /// at <c>PlayerLeftZone</c> processing or session TTL expiry"), resetting
        /// <c>LastBeatServerTick[slot]</c> back to <see cref="uint.MaxValue"/> and freeing the index
        /// for a future <see cref="AllocatePlayerSlot"/> call.
        /// </summary>
        /// <param name="playerEntityId">The departing player's entity id.</param>
        /// <exception cref="InvalidOperationException"><paramref name="playerEntityId"/> has no
        /// currently allocated slot.</exception>
        /// <example>
        /// <code>tracker.DeallocatePlayerSlot(playerEntityId: 555u);</code>
        /// </example>
        public void DeallocatePlayerSlot(uint playerEntityId)
        {
            if (!_playerEntityIdToSlot.TryGetValue(playerEntityId, out int slot))
            {
                throw new InvalidOperationException(
                    $"[LastBeatServerTickTracker] {nameof(DeallocatePlayerSlot)}: playerEntityId={playerEntityId} " +
                    "has no allocated slot.");
            }

            _lastBeatServerTick[slot] = uint.MaxValue;
            _playerSlotOccupied[slot] = false;
            _playerEntityIdToSlot.Remove(playerEntityId);
        }

        /// <summary>
        /// Returns <paramref name="playerEntityId"/>'s currently allocated slot index, if any —
        /// used by tests (AC-OWL-06a) to verify slot-index stability across a simulated reconnect
        /// cycle: the index returned here must be identical before and after ghost-period Beats,
        /// since nothing calls <see cref="DeallocatePlayerSlot"/> in between.
        /// </summary>
        /// <param name="playerEntityId">The player entity id to query.</param>
        /// <param name="slot">The player's current slot index, if allocated.</param>
        /// <returns><see langword="true"/> if <paramref name="playerEntityId"/> currently has an
        /// allocated slot.</returns>
        /// <example>
        /// <code>bool found = tracker.TryGetPlayerSlot(playerEntityId: 555u, out int slot);</code>
        /// </example>
        public bool TryGetPlayerSlot(uint playerEntityId, out int slot) =>
            _playerEntityIdToSlot.TryGetValue(playerEntityId, out slot);

        /// <summary>
        /// Allocates a mob slot in <c>[<see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/>,
        /// <see cref="TotalSlotCount"/>)</c> (CR-OWL-4: "Slot allocated at mob spawn"), preferring a
        /// free-list slot returned by a prior <see cref="DeallocateMobSlot"/> call over a
        /// never-used index. No entity-id key — mobs have no forward-dependency identity type in
        /// this codebase yet (Zone Instancing system, not yet built); the caller is responsible for
        /// associating the returned slot with its own mob entity id.
        /// </summary>
        /// <returns>The allocated slot index.</returns>
        /// <exception cref="InvalidOperationException">The mob region is fully exhausted — all
        /// <see cref="MAX_MOBS_PER_ZONE"/> slots are in use and the free list is empty.</exception>
        /// <example>
        /// <code>int mobSlot = tracker.AllocateMobSlot();</code>
        /// </example>
        public int AllocateMobSlot()
        {
            if (_freeMobSlots.Count > 0)
            {
                int slot = _freeMobSlots.Dequeue();
                _mobSlotState[slot - ZoneBufferPool.MAX_PLAYERS_PER_ZONE] = MobSlotState.InUse;
                return slot;
            }

            if (_nextUnusedMobIndex >= MAX_MOBS_PER_ZONE)
            {
                throw new InvalidOperationException(
                    $"[LastBeatServerTickTracker] {nameof(AllocateMobSlot)}: mob region exhausted — all " +
                    $"{MAX_MOBS_PER_ZONE} mob slots are in use and the free list is empty.");
            }

            int mobIndex = _nextUnusedMobIndex++;
            _mobSlotState[mobIndex] = MobSlotState.InUse;
            return ZoneBufferPool.MAX_PLAYERS_PER_ZONE + mobIndex;
        }

        /// <summary>
        /// Deallocates a mob slot (CR-OWL-4: "Slot deallocated at mob death or despawn"), applying
        /// the sanitization invariant: <c>LastBeatServerTick[slot]</c> is reset to
        /// <see cref="uint.MaxValue"/> <i>before</i> the slot is returned to the free list (AC-OWL-06b)
        /// — so a subsequent <see cref="AllocateMobSlot"/> reallocation of this index never carries a
        /// stale tick from the previous occupant.
        /// </summary>
        /// <param name="slot">The mob slot to deallocate, previously returned by
        /// <see cref="AllocateMobSlot"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside the mob
        /// region <c>[<see cref="ZoneBufferPool.MAX_PLAYERS_PER_ZONE"/>, <see cref="TotalSlotCount"/>)</c>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="slot"/> is not currently
        /// <see cref="MobSlotState.InUse"/> — either it is already in the free list (double-free
        /// guard), or it was never issued by <see cref="AllocateMobSlot"/> at all
        /// (deallocate-never-allocated guard, symmetric with <see cref="DeallocatePlayerSlot"/>'s
        /// equivalent guard). Code review finding: without the never-allocated guard, deallocating an
        /// unissued index pushes it onto the free list early, and when the fresh-index counter later
        /// reaches that same index via the normal allocation path, the slot could be handed out to two
        /// different mobs simultaneously.</exception>
        /// <example>
        /// <code>tracker.DeallocateMobSlot(mobSlot);</code>
        /// </example>
        public void DeallocateMobSlot(int slot)
        {
            int mobIndex = slot - ZoneBufferPool.MAX_PLAYERS_PER_ZONE;
            if (mobIndex < 0 || mobIndex >= MAX_MOBS_PER_ZONE)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot,
                    $"[LastBeatServerTickTracker] {nameof(DeallocateMobSlot)}: slot must be in the mob region " +
                    $"[{ZoneBufferPool.MAX_PLAYERS_PER_ZONE}, {TotalSlotCount}).");
            }

            // Rejects both double-free (state == Free) and deallocate-never-allocated
            // (state == NeverAllocated) in one check — symmetric with DeallocatePlayerSlot's
            // "never allocated" guard (see this method's <exception> doc comment).
            if (_mobSlotState[mobIndex] != MobSlotState.InUse)
            {
                throw new InvalidOperationException(
                    $"[LastBeatServerTickTracker] {nameof(DeallocateMobSlot)}: slot={slot} is not currently allocated " +
                    $"(state={_mobSlotState[mobIndex]}).");
            }

            // Sanitization invariant (CR-OWL-4): reset BEFORE returning to the free list.
            _lastBeatServerTick[slot] = uint.MaxValue;
            _mobSlotState[mobIndex] = MobSlotState.Free;
            _freeMobSlots.Enqueue(slot);
        }

        /// <summary>
        /// Looks up and validates <paramref name="slot"/> against <see cref="TotalSlotCount"/>,
        /// throwing <see cref="ArgumentOutOfRangeException"/> if out of range — the shared guard
        /// clause for every raw-slot-index method (<see cref="RecordBeat"/>,
        /// <see cref="TryGetLastBeatTick"/>, <see cref="IsWithinWrapCorrectionWindow"/>), matching
        /// this codebase's established guard-clause convention (e.g. <see cref="ZoneBufferPool.Release"/>).
        /// </summary>
        private void ValidateSlot(int slot, string paramName, string callerName)
        {
            if (slot < 0 || slot >= _lastBeatServerTick.Length)
            {
                throw new ArgumentOutOfRangeException(paramName, slot,
                    $"[LastBeatServerTickTracker] {callerName}: slot must be in [0, {_lastBeatServerTick.Length}) " +
                    $"(was {slot}).");
            }
        }
    }
}
