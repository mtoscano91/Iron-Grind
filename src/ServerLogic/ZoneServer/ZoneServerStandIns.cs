using System;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.EquipmentSystem;
using IronGrind.ItemDatabase;
using IronGrind.InventorySystem;
using IronGrind.Networking;
using UnityEngine;
using UnityEngine.AI;
using CharacterID = IronGrind.Currency.CharacterID;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Logs "[ZoneServer] &lt;step&gt; not implemented" the first time it is asked, then stays silent.
    /// One instance per stand-in, so the log is once per stand-in instance across all its methods.
    /// </summary>
    internal sealed class StandInLogOnce
    {
        private readonly string _step;
        private bool _logged;

        public StandInLogOnce(string step)
        {
            _step = step;
        }

        public void Log()
        {
            if (_logged)
            {
                return;
            }

            _logged = true;
            Debug.Log("[ZoneServer] " + _step + " not implemented");
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IConnectionCharacterDirectory"/> until the session epic ships: finds no character for
    /// any connection, so the dispatcher drops every request on its <c>NoCharacter</c> path. Logs
    /// "[ZoneServer] connection character directory not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// bool found = new StandInConnectionCharacterDirectory().TryGetCharacterId(5u, out CharacterID id); // false
    /// </code>
    /// </example>
    public sealed class StandInConnectionCharacterDirectory : IConnectionCharacterDirectory
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("connection character directory");

        /// <inheritdoc/>
        public bool TryGetCharacterId(uint clientId, out IronGrind.Currency.CharacterID charId)
        {
            _log.Log();
            charId = IronGrind.Currency.CharacterID.Invalid;
            return false;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IConnectionActivitySink"/> until the session epic ships: records nothing. Logs
    /// "[ZoneServer] connection activity sink not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInConnectionActivitySink().OnInboundActivity(5u, 100u); // logs once, records nothing
    /// </code>
    /// </example>
    public sealed class StandInConnectionActivitySink : IConnectionActivitySink
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("connection activity sink");

        /// <inheritdoc/>
        public void OnInboundActivity(uint clientId, uint serverTick)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneNavigation"/> until the Navigation epic ships (ADR-009 A1 Decision 2).
    /// Logs "[ZoneServer] navigation not implemented" once and does nothing else.
    /// </summary>
    /// <example>
    /// <code>
    /// var nav = new StandInZoneNavigation();
    /// nav.Teardown(); // logs once
    /// </code>
    /// </example>
    public sealed class StandInZoneNavigation : IZoneNavigation
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("navigation");

        /// <inheritdoc/>
        public void Initialize(ZoneID zone, NavMeshDataInstance instance)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public void Teardown()
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneBoundsValidator"/> (no longer used by the host, which uses ZoneDefinitionBoundsValidator): logs "[ZoneServer] bounds validation not implemented"
    /// once and always returns true.
    /// </summary>
    /// <example>
    /// <code>
    /// bool ok = new StandInZoneBoundsValidator().Validate(zone); // true
    /// </code>
    /// </example>
    public sealed class StandInZoneBoundsValidator : IZoneBoundsValidator
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("bounds validation");

        /// <inheritdoc/>
        public bool Validate(ZoneID zone)
        {
            _log.Log();
            return true;
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IGatewayRegistration"/>: logs "[ZoneServer] gateway registration not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInGatewayRegistration().RegisterZone(zone, 7777);
    /// </code>
    /// </example>
    public sealed class StandInGatewayRegistration : IGatewayRegistration
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("gateway registration");

        /// <inheritdoc/>
        public void RegisterZone(ZoneID zone, ushort port)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneCloseSteps"/>: logs "[ZoneServer] zone close steps not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneCloseSteps().RunCloseSteps();
    /// </code>
    /// </example>
    public sealed class StandInZoneCloseSteps : IZoneCloseSteps
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("zone close steps");

        /// <inheritdoc/>
        public void RunCloseSteps()
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneStateOwner"/>: logs "[ZoneServer] zone state not implemented" once.
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneStateOwner().MarkActive(zone);
    /// </code>
    /// </example>
    public sealed class StandInZoneStateOwner : IZoneStateOwner
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("zone state");

        /// <inheritdoc/>
        public void MarkActive(ZoneID zone)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public void MarkClosedInvalidBounds(ZoneID zone)
        {
            _log.Log();
        }
    }

    /// <summary>
    /// Stand-in for <see cref="IInventoryService"/> in the zone composition (Zone Instancing Story 014). The session end
    /// coordinator only calls <see cref="UnregisterCharacter"/>; it logs "[ZoneServer] inventory service not implemented"
    /// once and does nothing. Every other member throws <see cref="NotSupportedException"/>. The real
    /// <c>InventoryService</c> needs the item database wiring (inventory epic).
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneInventoryService().UnregisterCharacter(characterId); // logs once
    /// </code>
    /// </example>
    public sealed class StandInZoneInventoryService : IInventoryService
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("inventory service");

        /// <inheritdoc/>
        public event Action<InventoryChangedEventArgs> OnInventoryChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public event Action<InventoryFullEventArgs> OnInventoryFull
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public void UnregisterCharacter(CharacterID charId)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public void RegisterCharacter(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public InventorySlot GetSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool IsFull(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public int FilledSlots(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool HasFreeSlot(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool IsSlotLocked(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool HasItem(CharacterID charId, ItemID itemId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public PickupResult Pickup(CharacterID characterId, ItemID itemId, int quantity) => throw new NotSupportedException();

        /// <inheritdoc/>
        public void LockSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public void UnlockSlot(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public void RemoveItem(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public DiscardResult Discard(CharacterID charId, int slotIndex, int quantity) => throw new NotSupportedException();

        /// <inheritdoc/>
        public MoveResult Move(CharacterID charId, int fromSlot, int toSlot) => throw new NotSupportedException();

        /// <inheritdoc/>
        public MoveItemOutResult MoveItemOut(CharacterID charId, int slotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public MoveItemInResult MoveItemIn(CharacterID charId, ItemID itemId, byte enhancementLevel) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool ForceInsert(CharacterID charId, ItemID itemId, byte enhancementLevel) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool SetEnhancementLevel(CharacterID charId, int slotIndex, byte level) => throw new NotSupportedException();

        /// <inheritdoc/>
        public SellItemResult SellItem(CharacterID charId, int slotIndex, ItemID itemId, int quantity) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ConsumeItemResult ConsumeItem(CharacterID charId, ItemID itemId, int quantity) => throw new NotSupportedException();

        /// <inheritdoc/>
        public InventorySnapshot ExportSnapshot(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool ImportSnapshot(CharacterID charId, InventorySnapshot? snapshot) => throw new NotSupportedException();
    }

    /// <summary>
    /// Stand-in for <see cref="IEquipmentService"/> in the zone composition (Equipment Story 008). The session end
    /// coordinator only calls <see cref="UnregisterCharacter"/>; it logs "[ZoneServer] equipment service not implemented"
    /// once and does nothing. Every other member throws <see cref="NotSupportedException"/>. The real
    /// <c>EquipmentService</c> needs Character Stats, the item database and the real inventory (server bootstrap).
    /// </summary>
    /// <example>
    /// <code>
    /// new StandInZoneEquipmentService().UnregisterCharacter(characterId); // logs once
    /// </code>
    /// </example>
    public sealed class StandInZoneEquipmentService : IEquipmentService
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("equipment service");

        /// <inheritdoc/>
        public event Action<EquipmentAppearanceChangedEventArgs> OnAppearanceChanged
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public void UnregisterCharacter(CharacterID charId)
        {
            _log.Log();
        }

        /// <inheritdoc/>
        public byte GetAppearanceFlags(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public void RegisterCharacter(CharacterID charId, EntityID entityId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public EquipmentSlotState GetEquipmentSlotState(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

        /// <inheritdoc/>
        public byte GetSlotEnhancementLevel(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool IsSlotTransitioning(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ItemID GetEquippedWeaponItemID(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public byte GetEquippedWeaponEnhancementLevel(CharacterID charId) => throw new NotSupportedException();

        /// <inheritdoc/>
        public EquipResult Equip(CharacterID charId, GearSlot slot, ItemID itemId, int inventorySlotIndex) => throw new NotSupportedException();

        /// <inheritdoc/>
        public EquipResult Unequip(CharacterID charId, GearSlot slot) => throw new NotSupportedException();

        /// <inheritdoc/>
        public MergeResult RequestMerge(CharacterID charId, int slotIdx1, int slotIdx2, int slotIdx3) => throw new NotSupportedException();

        /// <inheritdoc/>
        public GearRestoreResult RestoreSlots(CharacterID charId, ReadOnlySpan<SavedGearSlot> slots) => throw new NotSupportedException();

        /// <inheritdoc/>
        public SavedGearSlot[] ExportSlots(CharacterID charId) => throw new NotSupportedException();
    }

    /// <summary>
    /// Stand-in for <see cref="IZoneStaticDataLoader"/>: logs "[ZoneServer] static data loading not implemented"
    /// once and returns true.
    /// </summary>
    /// <example>
    /// <code>
    /// bool ok = new StandInZoneStaticDataLoader().LoadAndValidate(); // true
    /// </code>
    /// </example>
    public sealed class StandInZoneStaticDataLoader : IZoneStaticDataLoader
    {
        private readonly StandInLogOnce _log = new StandInLogOnce("static data loading");

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            _log.Log();
            return true;
        }
    }
}
