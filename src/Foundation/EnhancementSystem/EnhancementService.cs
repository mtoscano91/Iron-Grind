using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Enhancement System entry point (design/gdd/enhancement-system.md CR-ENH-15). This story adds
    /// the validation step only (<see cref="ValidateAttempt"/>); Story 004 adds the attempt
    /// sequence (lock, scroll, draw, apply) and Story 005 the commit and rollback.
    /// Depends on injected interfaces only; validation reads state and mutates nothing.
    /// Enhancement Story 003.
    /// </summary>
    public sealed class EnhancementService
    {
        private readonly IInventoryService _inventory;
        private readonly IItemDatabase _itemDatabase;
        private readonly EnhancementConfig _config;
        private readonly INpcInteractionSessions _npcSessions;

        /// <summary>Creates the service over its collaborators.</summary>
        /// <param name="inventory">Bag reads (slot contents, lock flags).</param>
        /// <param name="itemDatabase">Item definitions (upgradeable flag, gear data, scroll data).</param>
        /// <param name="config">Level cap (CR-ENH-2).</param>
        /// <param name="npcSessions">NPC session query (CR-ENH-17).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public EnhancementService(
            IInventoryService inventory, IItemDatabase itemDatabase, EnhancementConfig config, INpcInteractionSessions npcSessions)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (itemDatabase == null) throw new ArgumentNullException(nameof(itemDatabase));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (npcSessions == null) throw new ArgumentNullException(nameof(npcSessions));
            _inventory = inventory;
            _itemDatabase = itemDatabase;
            _config = config;
            _npcSessions = npcSessions;
        }

        /// <summary>
        /// True iff an attempt is already in flight for <paramref name="charId"/> (CR-ENH-18). In
        /// Story 003 this is always false; Story 004 adds the flag.
        /// </summary>
        /// <param name="charId">The character to query.</param>
        public bool IsAttemptInProgress(CharacterID charId)
        {
            return false;
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
