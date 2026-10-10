using System;
using System.Collections.Generic;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.InventorySystem
{
    /// <summary>
    /// Sends the owning client its bag (Inventory Story 012, networking-wire-protocol.md TD-046 amendment):
    /// one <see cref="InventorySlotUpdate"/> per <see cref="IInventoryService.OnInventoryChanged"/> event and one
    /// <see cref="InventoryFullSync"/> per zone entry, both through <see cref="IClientMessageOutbox"/> with
    /// <c>isCapExempt</c> false and only to the character's own client. Implements the hold of the
    /// <c>InventorySlotUpdate</c> / <c>InventoryFullSync</c> notes (CR-ENH-11) as <see cref="IOwnerInventorySyncHold"/>.
    /// Tick thread only; not thread-safe.
    /// </summary>
    /// <remarks>
    /// <para>In production the story that sends <c>SessionReady</c> calls <see cref="OnSessionReadySent"/> right after
    /// it, and the connection layer calls <see cref="OnClientDisconnected"/>; this class keeps its own character to
    /// client map.</para>
    /// <para><b>Allocation.</b> Encode buffers are allocated at construction. Per-character state (including hold
    /// storage of <see cref="MAX_HELD_UPDATES_PER_CHARACTER"/> x <c>INVENTORY_SLOT_COUNT</c> entries) is allocated when the
    /// character's session becomes ready (<see cref="OnSessionReadySent"/>) or <see cref="OpenHold"/> is called for it
    /// and no state exists yet. It is freed as soon as the character has neither a client nor an open hold (on
    /// disconnect, on release or discard of the hold, or when another character takes over its client id); a state
    /// with an open hold is always kept, also across a disconnect. Outside a hold a bag-change event allocates
    /// nothing.</para>
    /// <para><b>Outbox contract.</b> <see cref="IClientMessageOutbox.Enqueue"/> is assumed not to throw. If it threw
    /// during <see cref="ReleaseHold"/> the remaining held updates would be lost, and if it threw in the
    /// <see cref="IInventoryService.OnInventoryChanged"/> handler later subscribers would be skipped. The transport
    /// adapter story (ADR-014), which writes the production outbox, must state whether it can throw (code review
    /// 2026-10-09, left as is by user decision).</para>
    /// <para><b>Slot indices.</b> The sender forwards the service's entries unchanged and does not remove a repeated
    /// slot index; "no slot index twice in one message" rests on <c>InventoryService</c> recording each slot once per
    /// event.</para>
    /// </remarks>
    public sealed class OwnerInventorySyncSender : IOwnerInventorySyncHold, IDisposable
    {
        /// <summary>
        /// Most updates one hold keeps. An enhancement attempt raises at most a few events; the wire protocol states no
        /// bound. A hold that exceeds it is replaced by one <see cref="InventoryFullSync"/> at release, which is correct
        /// because both messages carry absolute state.
        /// </summary>
        public const int MAX_HELD_UPDATES_PER_CHARACTER = 8;

        private const string LOG_PREFIX = "[OwnerInventorySyncSender]";

        private sealed class CharacterState
        {
            public uint ClientId;
            public bool HasClient;
            public bool HoldOpen;
            public bool FullSyncDeferred;
            public bool Overflowed;
            public bool OverflowLogged;
            public int HeldCount;
            public readonly int[] HeldEntryCounts = new int[MAX_HELD_UPDATES_PER_CHARACTER];
            public readonly InventorySlotEntry[] HeldEntries =
                new InventorySlotEntry[MAX_HELD_UPDATES_PER_CHARACTER * InventoryConstants.INVENTORY_SLOT_COUNT];
        }

        private readonly IInventoryService _inventory;
        private readonly IClientMessageOutbox _outbox;
        private readonly Dictionary<CharacterID, CharacterState> _states = new Dictionary<CharacterID, CharacterState>();
        private readonly Dictionary<uint, CharacterID> _characterByClient = new Dictionary<uint, CharacterID>();
        private readonly byte[] _encodeBuffer = new byte[InventorySlotUpdate.MaxBodySize];
        private readonly InventorySlotEntry[] _scratch = new InventorySlotEntry[InventoryConstants.INVENTORY_SLOT_COUNT];
        private bool _disposed;

        /// <summary>Creates the sender and subscribes to <see cref="IInventoryService.OnInventoryChanged"/>.</summary>
        /// <param name="inventory">The bag source; not null.</param>
        /// <param name="outbox">Server-to-client send seam; not null.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public OwnerInventorySyncSender(IInventoryService inventory, IClientMessageOutbox outbox)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _inventory.OnInventoryChanged += HandleInventoryChanged;
        }

        /// <summary>Unsubscribes from <see cref="IInventoryService.OnInventoryChanged"/>; afterwards a bag change sends nothing.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inventory.OnInventoryChanged -= HandleInventoryChanged;
        }

        /// <summary>
        /// Records that <paramref name="characterId"/>'s zone entry completed on <paramref name="clientId"/> (call right
        /// after <c>SessionReady</c> is sent). Replaces an earlier mapping of either id. With no hold open, enqueues one
        /// <see cref="InventoryFullSync"/> for the client. With a hold open, sends nothing, defers the sync to
        /// <see cref="ReleaseHold"/> and drops the updates the hold had collected.
        /// </summary>
        /// <remarks>
        /// Precondition: the character is registered with the inventory service before this call. For an unregistered
        /// character <see cref="IInventoryService.GetSlot"/> logs errors and the sync would list every slot empty
        /// (documented precondition, not checked). A repeated call for the same pair sends the full sync again, which is
        /// harmless because the message carries absolute state.
        /// </remarks>
        /// <param name="clientId">The connection that completed the zone entry.</param>
        /// <param name="characterId">The character that connection controls.</param>
        public void OnSessionReadySent(uint clientId, CharacterID characterId)
        {
            CharacterState state = GetOrCreateState(characterId);

            if (_characterByClient.TryGetValue(clientId, out CharacterID previousCharacter) && !previousCharacter.Equals(characterId)
                && _states.TryGetValue(previousCharacter, out CharacterState previousState))
            {
                DetachClient(previousState);
                FreeStateIfUnused(previousCharacter, previousState);
            }

            if (state.HasClient && state.ClientId != clientId)
            {
                _characterByClient.Remove(state.ClientId);
            }

            state.ClientId = clientId;
            state.HasClient = true;
            _characterByClient[clientId] = characterId;
            state.HeldCount = 0;
            state.Overflowed = false;

            if (state.HoldOpen)
            {
                state.FullSyncDeferred = true;
                return;
            }

            state.FullSyncDeferred = false;
            SendFullSync(clientId, characterId);
        }

        /// <summary>
        /// Removes the mappings of <paramref name="clientId"/>; nothing is enqueued for it afterwards. An open hold of its
        /// character stays open, but the updates it collected and a deferred full sync are dropped.
        /// </summary>
        /// <param name="clientId">The connection that closed.</param>
        public void OnClientDisconnected(uint clientId)
        {
            if (!_characterByClient.TryGetValue(clientId, out CharacterID characterId))
            {
                return;
            }

            _characterByClient.Remove(clientId);
            if (_states.TryGetValue(characterId, out CharacterState state))
            {
                DetachClient(state);
                FreeStateIfUnused(characterId, state);
            }
        }

        /// <summary>
        /// Opens a hold for the character, with or without a connected client. Opening an already open hold logs one
        /// warning and keeps the existing hold (its collected updates are not reset).
        /// </summary>
        /// <param name="characterId">The character whose owner sync is held.</param>
        public void OpenHold(CharacterID characterId)
        {
            CharacterState state = GetOrCreateState(characterId);
            if (state.HoldOpen)
            {
                Debug.LogWarning($"{LOG_PREFIX} OpenHold: a hold is already open for character={characterId}; keeping the existing hold.");
                return;
            }

            state.HoldOpen = true;
            state.FullSyncDeferred = false;
            state.Overflowed = false;
            state.OverflowLogged = false;
            state.HeldCount = 0;
        }

        /// <summary>
        /// Closes the hold. No session-ready client: nothing is sent. A deferred full sync or an overflow: exactly one
        /// <see cref="InventoryFullSync"/> built from the bag now. Otherwise the held updates, in the order raised, one
        /// enqueue each. Releasing with no open hold logs one warning and sends nothing.
        /// </summary>
        /// <param name="characterId">The character whose hold is released.</param>
        public void ReleaseHold(CharacterID characterId)
        {
            if (!_states.TryGetValue(characterId, out CharacterState state) || !state.HoldOpen)
            {
                Debug.LogWarning($"{LOG_PREFIX} ReleaseHold: no hold is open for character={characterId}; nothing sent.");
                return;
            }

            state.HoldOpen = false;
            bool sendFullSync = state.FullSyncDeferred || state.Overflowed;
            int heldCount = state.HeldCount;
            state.FullSyncDeferred = false;
            state.Overflowed = false;
            state.HeldCount = 0;

            if (!state.HasClient)
            {
                FreeStateIfUnused(characterId, state);
                return;
            }

            if (sendFullSync)
            {
                SendFullSync(state.ClientId, characterId);
                return;
            }

            for (int i = 0; i < heldCount; i++)
            {
                int count = state.HeldEntryCounts[i];
                var entries = new ReadOnlySpan<InventorySlotEntry>(state.HeldEntries, i * InventoryConstants.INVENTORY_SLOT_COUNT, count);
                int written = InventorySlotUpdateCodec.WriteBody(_encodeBuffer, entries);
                _outbox.Enqueue(state.ClientId, InventorySlotUpdate.MessageTypeId, new ReadOnlySpan<byte>(_encodeBuffer, 0, written), isCapExempt: false);
            }
        }

        /// <summary>
        /// Closes the hold and sends nothing (held updates, a deferred full sync and an overflow are dropped). A bag change
        /// after it returns is sent normally. Discarding with no open hold logs one warning.
        /// </summary>
        /// <remarks>
        /// Meant for the failed step 6b commit, where the client is disconnected (CR-NET-5.5) and gets its
        /// <see cref="InventoryFullSync"/> on the next zone entry. If a zone entry completed during the hold and the hold
        /// is discarded while that client stays connected, later updates would reach it with no full sync before them. A
        /// caller that ends a hold without a failed commit (CR-ENH-15 step 4 failure) calls <see cref="ReleaseHold"/>,
        /// not <see cref="DiscardHold"/>.
        /// </remarks>
        /// <param name="characterId">The character whose hold is discarded.</param>
        public void DiscardHold(CharacterID characterId)
        {
            if (!_states.TryGetValue(characterId, out CharacterState state) || !state.HoldOpen)
            {
                Debug.LogWarning($"{LOG_PREFIX} DiscardHold: no hold is open for character={characterId}; nothing to discard.");
                return;
            }

            state.HoldOpen = false;
            state.FullSyncDeferred = false;
            state.Overflowed = false;
            state.HeldCount = 0;
            FreeStateIfUnused(characterId, state);
        }

        private void HandleInventoryChanged(InventoryChangedEventArgs args)
        {
            if (!_states.TryGetValue(args.CharacterID, out CharacterState state) || !state.HasClient || args.Count < 1)
            {
                return;
            }

            if (state.HoldOpen)
            {
                HoldInventoryChange(state, args);
                return;
            }

            for (int i = 0; i < args.Count; i++)
            {
                _scratch[i] = ToEntry(args[i]);
            }

            int written = InventorySlotUpdateCodec.WriteBody(_encodeBuffer, new ReadOnlySpan<InventorySlotEntry>(_scratch, 0, args.Count));
            _outbox.Enqueue(state.ClientId, InventorySlotUpdate.MessageTypeId, new ReadOnlySpan<byte>(_encodeBuffer, 0, written), isCapExempt: false);
        }

        private static void HoldInventoryChange(CharacterState state, InventoryChangedEventArgs args)
        {
            if (state.FullSyncDeferred || state.Overflowed)
            {
                return;
            }

            if (state.HeldCount >= MAX_HELD_UPDATES_PER_CHARACTER)
            {
                state.Overflowed = true;
                state.HeldCount = 0;
                if (!state.OverflowLogged)
                {
                    state.OverflowLogged = true;
                    Debug.LogWarning($"{LOG_PREFIX} hold overflow: character={args.CharacterID} raised more than " +
                        $"{MAX_HELD_UPDATES_PER_CHARACTER} updates during one hold; one full sync replaces them at release.");
                }

                return;
            }

            int baseIndex = state.HeldCount * InventoryConstants.INVENTORY_SLOT_COUNT;
            for (int i = 0; i < args.Count; i++)
            {
                state.HeldEntries[baseIndex + i] = ToEntry(args[i]);
            }

            state.HeldEntryCounts[state.HeldCount] = args.Count;
            state.HeldCount++;
        }

        private void SendFullSync(uint clientId, CharacterID characterId)
        {
            for (int i = 0; i < InventoryConstants.INVENTORY_SLOT_COUNT; i++)
            {
                InventorySlot slot = _inventory.GetSlot(characterId, i);
                _scratch[i] = new InventorySlotEntry((byte)i, slot.ItemId, slot.Quantity, slot.EnhancementLevel);
            }

            int written = InventoryFullSyncCodec.WriteBody(_encodeBuffer, _scratch);
            _outbox.Enqueue(clientId, InventoryFullSync.MessageTypeId, new ReadOnlySpan<byte>(_encodeBuffer, 0, written), isCapExempt: false);
        }

        private static InventorySlotEntry ToEntry(SlotChange change)
        {
            return new InventorySlotEntry((byte)change.SlotIndex, change.ItemId, change.Quantity, change.EnhancementLevel);
        }

        private static void DetachClient(CharacterState state)
        {
            state.HasClient = false;
            state.HeldCount = 0;
            state.FullSyncDeferred = false;
            state.Overflowed = false;
        }

        // Frees the state of a character that has neither a client nor an open hold.
        private void FreeStateIfUnused(CharacterID characterId, CharacterState state)
        {
            if (!state.HasClient && !state.HoldOpen)
            {
                _states.Remove(characterId);
            }
        }

        private CharacterState GetOrCreateState(CharacterID characterId)
        {
            if (!_states.TryGetValue(characterId, out CharacterState state))
            {
                state = new CharacterState();
                _states.Add(characterId, state);
            }

            return state;
        }
    }
}
