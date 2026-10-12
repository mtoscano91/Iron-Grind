using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The persisted state of one character (CR-CP-2). Mutable data holder; the arrays have a fixed
    /// capacity and are never resized.
    /// </summary>
    /// <remarks>
    /// Buff modifiers, equipment modifier values, <c>PrestigeBand</c> / appearance flags and
    /// <c>IsTransitioning</c> are server-memory only and are deliberately not members (CR-CP-2).
    /// <see cref="Clone"/> makes the deep copy that ADR-011's snapshot rule needs. A record handed to a
    /// persistence call as its snapshot belongs to that call: the caller must not read or change it
    /// afterwards (ADR-011 Decision 3 — after its first <c>await</c> a method touches only its copy).
    /// </remarks>
    public sealed class CharacterRecord
    {
        /// <summary>Allocates the fixed-size gear and inventory arrays, all slots empty.</summary>
        public CharacterRecord()
        {
            GearSlots = new GearSlotRecord[CharacterPersistenceConstants.GEAR_SLOT_COUNT];
            InventorySlots = new ItemID[InventoryConstants.INVENTORY_SLOT_COUNT];
            InventoryItemCounts = new int[InventoryConstants.INVENTORY_SLOT_COUNT];
            InventoryEnhancementLevels = new byte[InventoryConstants.INVENTORY_SLOT_COUNT];
        }

        /// <summary>The character's id.</summary>
        public CharacterID CharacterID { get; set; }

        /// <summary>The owning account.</summary>
        public AccountID AccountID { get; set; }

        /// <summary>The character's name.</summary>
        public string CharacterName { get; set; }

        /// <summary>Raw class type (a byte until a class enum exists).</summary>
        public byte ClassType { get; set; }

        /// <summary>Character level.</summary>
        public int Level { get; set; }

        /// <summary>Experience points.</summary>
        public int Experience { get; set; }

        /// <summary>Unspent attribute points.</summary>
        public int HeldFreePoints { get; set; }

        /// <summary>Strength attribute.</summary>
        public int STR { get; set; }

        /// <summary>Dexterity attribute.</summary>
        public int DEX { get; set; }

        /// <summary>Vitality attribute.</summary>
        public int VIT { get; set; }

        /// <summary>Intelligence attribute.</summary>
        public int INT { get; set; }

        /// <summary>Base maximum hit points.</summary>
        public int MaxHP { get; set; }

        /// <summary>Base maximum mana points.</summary>
        public int MaxMP { get; set; }

        /// <summary>Base attack power.</summary>
        public int AttackPower { get; set; }

        /// <summary>Base defense.</summary>
        public int Defense { get; set; }

        /// <summary>Base magic defense.</summary>
        public int MagicDefense { get; set; }

        /// <summary>Base critical hit chance.</summary>
        public float CritChance { get; set; }

        /// <summary>Base critical hit multiplier.</summary>
        public float CritMultiplier { get; set; }

        /// <summary>Base attack range.</summary>
        public float AttackRange { get; set; }

        /// <summary>Base attack speed multiplier.</summary>
        public float AttackSpeedMultiplier { get; set; }

        /// <summary>Base movement speed.</summary>
        public float MovementSpeed { get; set; }

        /// <summary>Current hit points.</summary>
        public float CurrentHP { get; set; }

        /// <summary>Current mana points.</summary>
        public float CurrentMP { get; set; }

        /// <summary>Gold balance.</summary>
        public uint GoldBalance { get; set; }

        /// <summary>Gold version, for optimistic concurrency.</summary>
        public uint GoldVersion { get; set; }

        /// <summary>The last zone the character was in; <see cref="ZoneID.Invalid"/> on first login (CR-CP-9).</summary>
        public ZoneID LastZoneID { get; set; }

        /// <summary>Record version, for optimistic concurrency.</summary>
        public uint SaveVersion { get; set; }

        /// <summary>When the record was created (UTC).</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>When the record was last saved (UTC).</summary>
        public DateTime LastSavedAtUtc { get; set; }

        /// <summary>Gear slots, indexed by <c>(int)GearSlot</c> (this type's convention; CR-CP-2 gives the count, not the order); length <see cref="CharacterPersistenceConstants.GEAR_SLOT_COUNT"/>.</summary>
        public GearSlotRecord[] GearSlots { get; }

        /// <summary>Inventory item per slot; length <see cref="InventoryConstants.INVENTORY_SLOT_COUNT"/>. Parallel to the two arrays below.</summary>
        public ItemID[] InventorySlots { get; }

        /// <summary>Inventory stack count per slot; parallel to <see cref="InventorySlots"/>.</summary>
        public int[] InventoryItemCounts { get; }

        /// <summary>Inventory enhancement level per slot; parallel to <see cref="InventorySlots"/>.</summary>
        public byte[] InventoryEnhancementLevels { get; }

        /// <summary>Returns a deep copy: every scalar copied, every array a new array with the same contents.</summary>
        public CharacterRecord Clone()
        {
            var copy = new CharacterRecord
            {
                CharacterID = CharacterID,
                AccountID = AccountID,
                CharacterName = CharacterName,
                ClassType = ClassType,
                Level = Level,
                Experience = Experience,
                HeldFreePoints = HeldFreePoints,
                STR = STR,
                DEX = DEX,
                VIT = VIT,
                INT = INT,
                MaxHP = MaxHP,
                MaxMP = MaxMP,
                AttackPower = AttackPower,
                Defense = Defense,
                MagicDefense = MagicDefense,
                CritChance = CritChance,
                CritMultiplier = CritMultiplier,
                AttackRange = AttackRange,
                AttackSpeedMultiplier = AttackSpeedMultiplier,
                MovementSpeed = MovementSpeed,
                CurrentHP = CurrentHP,
                CurrentMP = CurrentMP,
                GoldBalance = GoldBalance,
                GoldVersion = GoldVersion,
                LastZoneID = LastZoneID,
                SaveVersion = SaveVersion,
                CreatedAtUtc = CreatedAtUtc,
                LastSavedAtUtc = LastSavedAtUtc,
            };
            Array.Copy(GearSlots, copy.GearSlots, GearSlots.Length);
            Array.Copy(InventorySlots, copy.InventorySlots, InventorySlots.Length);
            Array.Copy(InventoryItemCounts, copy.InventoryItemCounts, InventoryItemCounts.Length);
            Array.Copy(InventoryEnhancementLevels, copy.InventoryEnhancementLevels, InventoryEnhancementLevels.Length);
            return copy;
        }
    }
}
