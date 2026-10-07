using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Enhancement System entry point (design/gdd/enhancement-system.md CR-ENH-15). Story 003 added
    /// validation (<see cref="ValidateAttempt"/>). Story 004 adds the two-phase attempt sequence
    /// (decided at readiness 2026-10-07): <see cref="BeginAttempt"/> runs steps 2-6a (validate, lock,
    /// consume scroll, draw, apply) and leaves a pending attempt; the caller commits; then
    /// <see cref="CompleteAttempt"/> runs steps 7-9 (unlock, outcome events and the +9 trigger, result;
    /// events added by Story 007). Story 005 adds the third call,
    /// <see cref="RollBackAttempt"/>, which ends a pending attempt by undoing it (CR-ENH-15 Rollback).
    /// An attempt ends through exactly one of <see cref="CompleteAttempt"/> or <see cref="RollBackAttempt"/>.
    /// Depends on injected interfaces only; the service is synchronous and never calls persistence.
    /// </summary>
    public sealed class EnhancementService
    {
        private readonly IInventoryService _inventory;
        private readonly IItemDatabase _itemDatabase;
        private readonly EnhancementConfig _config;
        private readonly INpcInteractionSessions _npcSessions;
        private readonly System.Random _random;
        private readonly Dictionary<CharacterID, PendingAttempt> _pending = new Dictionary<CharacterID, PendingAttempt>();

        // What Complete (and Story 005's rollback) needs for one in-flight attempt.
        private readonly struct PendingAttempt
        {
            public PendingAttempt(
                int itemSlotIndex, ItemID itemId, byte previousLevel, ItemID scrollItemId, EnhancementOutcome outcome, byte newLevel)
            {
                ItemSlotIndex = itemSlotIndex;
                ItemId = itemId;
                PreviousLevel = previousLevel;
                ScrollItemId = scrollItemId;
                Outcome = outcome;
                NewLevel = newLevel;
            }

            public int ItemSlotIndex { get; }
            public ItemID ItemId { get; }
            public byte PreviousLevel { get; }
            public ItemID ScrollItemId { get; }
            public EnhancementOutcome Outcome { get; }
            public byte NewLevel { get; }
        }

        /// <summary>
        /// Raised once per completed attempt whose outcome is Success (CR-ENH-15 steps 7-9, CR-ENH-11),
        /// from <see cref="CompleteAttempt"/> after the slot is unlocked. Server-side; the commit
        /// orchestrator (Story 011) calls <see cref="CompleteAttempt"/> only after a successful commit.
        /// Carries ids, not names. A subscriber's exception is logged and contained.
        /// </summary>
        public event Action<EnhancementSuccessEventArgs> OnEnhancementSuccess;

        /// <summary>
        /// Raised once per completed attempt whose outcome is Destruction (CR-ENH-15 steps 7-9, CR-ENH-11),
        /// from <see cref="CompleteAttempt"/> after the slot is unlocked. Server-side; carries ids, not
        /// names. A subscriber's exception is logged and contained.
        /// </summary>
        public event Action<EnhancementDestructionEventArgs> OnEnhancementDestruction;

        /// <summary>
        /// Raised from <see cref="CompleteAttempt"/>, after <see cref="OnEnhancementSuccess"/>, when a
        /// success reaches <see cref="EnhancementConstants.SERVER_BROADCAST_LEVEL"/> (CR-ENH-14, AC-ENH-18).
        /// Server-side trigger only: carries ids, not names; Story 010 resolves the display names for
        /// ServerBroadcast_Enhancement9 and delivery is best effort (EC-ENH-8). A subscriber's exception
        /// is logged and contained.
        /// </summary>
        public event Action<EnhancementBroadcastEventArgs> OnEnhancementBroadcastLevelReached;

        /// <summary>Creates the service over its collaborators.</summary>
        /// <param name="inventory">Bag reads and mutations (locks, scroll consumption, level, destruction).</param>
        /// <param name="itemDatabase">Item definitions (upgradeable flag, gear data, scroll data).</param>
        /// <param name="config">Level cap (CR-ENH-2) and outcome resolution (CR-ENH-9, CR-ENH-10).</param>
        /// <param name="npcSessions">NPC session query (CR-ENH-17).</param>
        /// <param name="random">Random source; exactly one <see cref="System.Random.NextDouble"/> per pending attempt.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EnhancementService(
            IInventoryService inventory, IItemDatabase itemDatabase, EnhancementConfig config,
            INpcInteractionSessions npcSessions, System.Random random)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (itemDatabase == null) throw new ArgumentNullException(nameof(itemDatabase));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (npcSessions == null) throw new ArgumentNullException(nameof(npcSessions));
            if (random == null) throw new ArgumentNullException(nameof(random));
            _inventory = inventory;
            _itemDatabase = itemDatabase;
            _config = config;
            _npcSessions = npcSessions;
            _random = random;
        }

        /// <summary>
        /// True iff a pending attempt is stored for <paramref name="charId"/> (CR-ENH-8, CR-ENH-18):
        /// from a pending <see cref="BeginAttempt"/> until <see cref="CompleteAttempt"/> or <see cref="RollBackAttempt"/>.
        /// </summary>
        /// <param name="charId">The character to query.</param>
        public bool IsAttemptInProgress(CharacterID charId)
        {
            return _pending.ContainsKey(charId);
        }

        /// <summary>
        /// Runs CR-ENH-15 steps 2-6a: validate; lock the item slot (CR-ENH-7); consume one scroll;
        /// draw once and resolve the outcome; apply it to the bag (level + 1, or destroy the item).
        /// The item slot stays locked until <see cref="CompleteAttempt"/> or <see cref="RollBackAttempt"/>. Two-phase design decided
        /// at readiness 2026-10-07. AC-ENH-8, 9, 10, 11, 12, 33, 36.
        /// </summary>
        /// <param name="charId">The requesting character.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        /// <returns>A rejection (nothing changed, no draw) or a pending attempt (outcome not to be sent to a client before the commit, CR-ENH-11).</returns>
        /// <exception cref="InvalidOperationException">Broken invariant: the level could not be set after the scroll was consumed. The slot is unlocked and no attempt is pending.</exception>
        /// <remarks>
        /// Any exception raised after the lock — including one thrown by an
        /// <c>OnInventoryChanged</c> subscriber during the scroll consumption or the level change —
        /// is rethrown after the item slot has been unlocked, with no attempt pending. Bag changes
        /// already made on that path (the consumed scroll, a level already set) are not undone.
        /// </remarks>
        public EnhancementAttemptStart BeginAttempt(CharacterID charId, int itemSlotIndex, int scrollSlotIndex)
        {
            EnhancementAttemptValidation validation = ValidateAttempt(charId, itemSlotIndex, scrollSlotIndex);
            if (!validation.IsValid)
                return EnhancementAttemptStart.Rejected(validation.RejectionCode);

            _inventory.LockSlot(charId, itemSlotIndex);
            try
            {
                if (!_inventory.ConsumeItem(charId, validation.ScrollItemId, 1).Success)
                {
                    _inventory.UnlockSlot(charId, itemSlotIndex);
                    return EnhancementAttemptStart.Rejected(EnhancementResultCode.RejectedScrollNotFound);
                }

                EnhancementOutcome outcome = _config.ResolveOutcome(validation.CurrentLevel, _random.NextDouble());
                byte newLevel = ApplyOutcome(charId, itemSlotIndex, validation, outcome);

                // Recorded last so that nothing above can leave a character stuck "in progress".
                _pending[charId] = new PendingAttempt(
                    itemSlotIndex, validation.ItemId, validation.CurrentLevel, validation.ScrollItemId, outcome, newLevel);
                return EnhancementAttemptStart.Pending(outcome, newLevel);
            }
            catch
            {
                // Nothing is pending on this path, so nothing else would ever unlock the slot: the
                // inventory calls above fire OnInventoryChanged, and a throwing subscriber's
                // exception propagates out of them. Unlock (a silent no-op if the slot was cleared),
                // then rethrow.
                _inventory.UnlockSlot(charId, itemSlotIndex);
                throw;
            }
        }

        /// <summary>
        /// Runs CR-ENH-15 steps 7-9 after a successful commit (the other way an attempt ends is
        /// <see cref="RollBackAttempt"/>, after a failed one): ends the attempt, unlocks the item slot
        /// (a silent no-op after a destruction), raises <see cref="OnEnhancementSuccess"/> or
        /// <see cref="OnEnhancementDestruction"/>, then <see cref="OnEnhancementBroadcastLevelReached"/>
        /// when a success reaches <see cref="EnhancementConstants.SERVER_BROADCAST_LEVEL"/> (CR-ENH-14,
        /// AC-ENH-18), and returns the final result (UI-ENH-2). A subscriber's exception is logged and
        /// contained (EC-ENH-8); the remaining events are still raised and the result is still returned.
        /// </summary>
        /// <param name="charId">The character whose pending attempt to complete.</param>
        /// <exception cref="InvalidOperationException">No attempt is pending for <paramref name="charId"/>.</exception>
        public EnhancementAttemptResult CompleteAttempt(CharacterID charId)
        {
            if (!_pending.TryGetValue(charId, out PendingAttempt attempt))
                throw new InvalidOperationException($"[EnhancementService] CompleteAttempt: no attempt is pending for {charId}.");

            _pending.Remove(charId);
            _inventory.UnlockSlot(charId, attempt.ItemSlotIndex);
            bool isSuccess = attempt.Outcome == EnhancementOutcome.Success;
            if (isSuccess)
                Raise(OnEnhancementSuccess, new EnhancementSuccessEventArgs(charId, attempt.ItemId, attempt.NewLevel));
            else
                Raise(OnEnhancementDestruction, new EnhancementDestructionEventArgs(charId, attempt.ItemId));

            if (isSuccess && attempt.NewLevel == EnhancementConstants.SERVER_BROADCAST_LEVEL)
                Raise(OnEnhancementBroadcastLevelReached, new EnhancementBroadcastEventArgs(charId, attempt.ItemId, attempt.NewLevel));

            EnhancementResultCode code = isSuccess ? EnhancementResultCode.Success : EnhancementResultCode.Destruction;
            return new EnhancementAttemptResult(attempt.Outcome, attempt.NewLevel, code);
        }

        // A subscriber's failure must not affect the attempt, the other events or the result (EC-ENH-8).
        // The exception is logged and contained. A multicast delegate stops at its first throwing
        // subscriber, so later subscribers of that same event are skipped (as in the Loot services).
        private static void Raise<TArgs>(Action<TArgs> handlers, in TArgs args)
        {
            if (handlers == null)
                return;

            try
            {
                handlers(args);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Undoes a pending attempt after a failed commit (CR-ENH-7, CR-ENH-11, CR-ENH-15 Rollback,
        /// EC-ENH-6, AC-ENH-23, AC-ENH-34; character-persistence.md CR-CP-5: rollback is caller-owned).
        /// Called by the commit orchestrator (Enhancement Story 011) when the commit reports any
        /// non-success result. Order: restore the item (level reset after a success, re-insert after a
        /// destruction), restore the scroll, unlock the item slot, log <c>CriticalEnhancementWriteFailed</c>.
        /// A failed restore call logs <c>CriticalEnhancementRollbackFailed</c> and the remaining steps
        /// still run. Does NOT disconnect the client, preserve the session or send a result; returns nothing.
        /// </summary>
        /// <param name="charId">The character whose pending attempt to roll back.</param>
        /// <exception cref="InvalidOperationException">No attempt is pending for <paramref name="charId"/>; nothing else happens.</exception>
        /// <remarks>
        /// The attempt always ends: the pending entry is removed, the slot unlocked and the critical
        /// write-failed error logged even if an inventory call throws (a subscriber's exception
        /// propagates out of it). The scroll restore still runs when the item restore throws; the
        /// exception then continues to the caller. Slot positions: after a destruction the item is restored into the
        /// lowest empty slot, so it may land in a different slot, and an emptied scroll stack's slot can
        /// be taken by the item. Contents are restored; positions may swap.
        /// </remarks>
        public void RollBackAttempt(CharacterID charId)
        {
            if (!_pending.TryGetValue(charId, out PendingAttempt attempt))
                throw new InvalidOperationException($"[EnhancementService] RollBackAttempt: no attempt is pending for {charId}.");

            try
            {
                // The scroll restore runs even if the item restore throws (a subscriber's exception
                // propagates out of inventory calls): one failing step must not cost the player the
                // other half of the rollback.
                try
                {
                    RestoreItem(charId, attempt);
                }
                finally
                {
                    RestoreScroll(charId, attempt);
                }
            }
            finally
            {
                // The attempt always ends, and the failed write is always logged — also when an
                // exception from a restore step is propagating.
                _pending.Remove(charId);
                _inventory.UnlockSlot(charId, attempt.ItemSlotIndex);
                Debug.LogError(
                    $"[EnhancementService] CriticalEnhancementWriteFailed: rolled back the attempt for {charId} (item {attempt.ItemId}, previous level {attempt.PreviousLevel}, scroll {attempt.ScrollItemId}).");
            }
        }

        // Rollback step 1. Logs and continues if the inventory refuses.
        private void RestoreItem(CharacterID charId, PendingAttempt attempt)
        {
            bool restored = attempt.Outcome == EnhancementOutcome.Success
                ? _inventory.SetEnhancementLevel(charId, attempt.ItemSlotIndex, attempt.PreviousLevel)
                : _inventory.ForceInsert(charId, attempt.ItemId, attempt.PreviousLevel);
            if (!restored)
            {
                Debug.LogError(
                    $"[EnhancementService] CriticalEnhancementRollbackFailed: item restore failed for {charId}: item {attempt.ItemId} at level {attempt.PreviousLevel}, scroll {attempt.ScrollItemId}. Manual restoration required.");
            }
        }

        // Rollback step 2. Logs and continues if the inventory refuses.
        private void RestoreScroll(CharacterID charId, PendingAttempt attempt)
        {
            if (!_inventory.Pickup(charId, attempt.ScrollItemId, 1).Success)
            {
                Debug.LogError(
                    $"[EnhancementService] CriticalEnhancementRollbackFailed: scroll restore failed for {charId}: item {attempt.ItemId} at level {attempt.PreviousLevel}, scroll {attempt.ScrollItemId}. Manual restoration required.");
            }
        }

        // Step 6a. Returns the new level (0 on destruction). Throws if the level cannot be set;
        // BeginAttempt's catch unlocks the slot.
        private byte ApplyOutcome(CharacterID charId, int itemSlotIndex, EnhancementAttemptValidation validation, EnhancementOutcome outcome)
        {
            if (outcome == EnhancementOutcome.Destruction)
            {
                _inventory.RemoveItem(charId, itemSlotIndex);
                return 0;
            }

            byte newLevel = (byte)(validation.CurrentLevel + 1);
            if (!_inventory.SetEnhancementLevel(charId, itemSlotIndex, newLevel))
            {
                throw new InvalidOperationException(
                    $"[EnhancementService] BeginAttempt: SetEnhancementLevel failed for {charId}, slot {itemSlotIndex}, item {validation.ItemId} after the scroll was consumed.");
            }
            return newLevel;
        }

        /// <summary>
        /// Validates an attempt (CR-ENH-15 step 2). Check order, first failure wins (decided at
        /// readiness 2026-10-07): NPC session, attempt in progress, item exists, slot unlocked,
        /// upgradeable, not an accessory, below max level, scroll exists, tier match. Reads only;
        /// logs an error solely for an item the Item Database cannot resolve.
        /// </summary>
        /// <param name="charId">The requesting character.</param>
        /// <param name="itemSlotIndex">Bag slot of the item to enhance.</param>
        /// <param name="scrollSlotIndex">Bag slot of the scroll.</param>
        internal EnhancementAttemptValidation ValidateAttempt(CharacterID charId, int itemSlotIndex, int scrollSlotIndex)
        {
            if (!_npcSessions.IsActive(charId))
                return EnhancementAttemptValidation.Rejected(EnhancementResultCode.RejectedNoNPCSession);
            if (IsAttemptInProgress(charId))
                return EnhancementAttemptValidation.Rejected(EnhancementResultCode.RejectedConcurrentAttempt);

            EnhancementResultCode code = CheckItem(charId, itemSlotIndex, out InventorySlot itemSlot, out ItemDefinition itemDef);
            if (code != EnhancementResultCode.Success)
                return EnhancementAttemptValidation.Rejected(code);

            code = CheckScroll(charId, scrollSlotIndex, itemDef.EquipmentData.GearTier, out InventorySlot scrollSlot);
            if (code != EnhancementResultCode.Success)
                return EnhancementAttemptValidation.Rejected(code);

            return EnhancementAttemptValidation.Valid(itemSlot.ItemId, itemSlot.EnhancementLevel, scrollSlot.ItemId);
        }

        // Steps 3-8 of the check order. Returns Success when the item may be enhanced.
        private EnhancementResultCode CheckItem(CharacterID charId, int slotIndex, out InventorySlot slot, out ItemDefinition def)
        {
            slot = InventorySlot.Empty;
            def = null;
            if (!IsInRange(slotIndex)) return EnhancementResultCode.RejectedItemNotFound;

            slot = _inventory.GetSlot(charId, slotIndex);
            if (slot.IsEmpty) return EnhancementResultCode.RejectedItemNotFound;
            if (_inventory.IsSlotLocked(charId, slotIndex)) return EnhancementResultCode.RejectedConcurrentAttempt;

            if (!_itemDatabase.TryGetItem(slot.ItemId, out def) || def == null)
            {
                Debug.LogError($"[EnhancementService] ValidateAttempt: slot {slotIndex} for {charId} holds {slot.ItemId}, which the Item Database cannot resolve.");
                return EnhancementResultCode.RejectedItemNotFound;
            }

            // CR-ENH-4/5: IsUpgradeable alone is not enough; the gear checks below read EquipmentData.
            if (!def.IsUpgradeable || def.EquipmentData == null) return EnhancementResultCode.RejectedNotUpgradeable;

            GearSlot gearSlot = def.EquipmentData.GearSlot;
            if (gearSlot == GearSlot.Ring || gearSlot == GearSlot.Necklace) return EnhancementResultCode.RejectedAccessoryType;
            if (slot.EnhancementLevel >= _config.MaxEnhancementLevel) return EnhancementResultCode.RejectedAtMaxLevel;
            return EnhancementResultCode.Success;
        }

        // Steps 9-10 of the check order. Returns Success when the scroll exists and matches the item's tier.
        private EnhancementResultCode CheckScroll(CharacterID charId, int slotIndex, GearTier itemTier, out InventorySlot slot)
        {
            slot = InventorySlot.Empty;
            if (!IsInRange(slotIndex)) return EnhancementResultCode.RejectedScrollNotFound;

            slot = _inventory.GetSlot(charId, slotIndex);
            if (slot.IsEmpty) return EnhancementResultCode.RejectedScrollNotFound;

            if (!_itemDatabase.TryGetItem(slot.ItemId, out ItemDefinition scrollDef) || scrollDef == null)
            {
                Debug.LogError($"[EnhancementService] ValidateAttempt: scroll slot {slotIndex} for {charId} holds {slot.ItemId}, which the Item Database cannot resolve.");
                return EnhancementResultCode.RejectedScrollNotFound;
            }

            // Item Database Rule 13: an item is a scroll iff ScrollData is non-null.
            if (scrollDef.ScrollData == null) return EnhancementResultCode.RejectedScrollNotFound;
            if (scrollDef.ScrollData.TargetGearTier != itemTier) return EnhancementResultCode.RejectedTierMismatch;
            return EnhancementResultCode.Success;
        }

        // Range-checked before any inventory read: its reads log an error for an out-of-range index.
        private static bool IsInRange(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < InventoryConstants.INVENTORY_SLOT_COUNT;
        }
    }
}
