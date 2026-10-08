namespace IronGrind.Tests.EditMode.Architecture
{
    /// <summary>
    /// Data for <see cref="AssemblyBoundary_Tests"/> (ADR-012 Decision 6, check 1). Every top-level type
    /// defined in <c>IronGrind.Foundation</c> must be on exactly one of the two lists below, as a full
    /// reflection name (<c>Namespace.TypeName</c>; generic types carry their arity suffix, for example
    /// <c>`1</c>). Nested and compiler-generated types are matched through their declaring type.
    /// Move stories edit this file, not the test logic.
    /// </summary>
    internal static class AssemblyBoundaryLists
    {
        /// <summary>
        /// Types that belong in <c>IronGrind.Foundation</c> by ADR-012 Decision 3 rule 3 or Decision 7.
        /// An entry is added only with its client consumer named in a comment beside it.
        /// </summary>
        internal static readonly string[] SharedAllowList =
        {
            // Ids used by client screens and wire messages. "planned" marks a consumer that is not built
            // yet: the type is shared by ADR-012 Decision 6 because it appears in wire messages.
            "IronGrind.CharacterStats.EntityID",   // consumer: LevelingHudController, RespecScreenPresenter, LevelUpOverlayPresenter
            "IronGrind.CharacterStats.ItemID",     // consumer (planned): inventory and item tooltip screens
            "IronGrind.CharacterStats.StatID",     // consumer: RespecScreenPresenter, PlayerResourceClusterPresenter
            "IronGrind.Currency.CharacterID",      // consumer (planned): client session and gold display (wire id)
            "IronGrind.Currency.GoldTransactionReason", // consumer (planned): gold sync event display (wire enum)

            // Item Database: static definition data both sides load (whole folder is shared, ADR-012
            // Decision 6). The consumer of every entry below is planned, not built.
            "IronGrind.ItemDatabase.ConsumableData",        // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.EffectType",            // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ElementType",           // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.EquipmentData",         // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.GearSlot",              // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.GearTier",              // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.IItemDatabase",         // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ItemCategory",          // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ItemDatabase",          // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ItemDatabaseSeeder",    // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ItemDefinition",        // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ValidationSeverity",    // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ValidationIssue",       // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ValidationResult",      // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ItemDefinitionValidator", // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.MvpItemRecordData",     // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.ScrollData",            // consumer: item tooltip and inventory screens
            "IronGrind.ItemDatabase.StatModifierEntry",     // consumer: item tooltip and inventory screens

            // Inventory: enums that networking-wire-protocol.md defines as wire enums.
            "IronGrind.InventorySystem.DiscardFailReason", // consumer (planned): inventory screen (wire enum, DiscardResult message)
            "IronGrind.InventorySystem.MoveFailReason",    // consumer (planned): inventory screen (wire enum, MoveResult message)

            // Class definitions shown by the respec flow.
            "IronGrind.LevelingSystem.ClassDefinition", // consumer: RespecScreenPresenter
            "IronGrind.LevelingSystem.IClassRegistry",  // consumer: RespecScreenPresenter
            "IronGrind.LevelingSystem.ClassRegistry",   // consumer: RespecScreenPresenter

            // Leveling: client read model (ADR-012 Decision 7).
            "IronGrind.LevelingSystem.LevelingDisplayFormulas", // consumer: LevelUpOverlayPresenter, RespecScreenPresenter
            "IronGrind.LevelingSystem.ILocalPlayerLevelingView", // consumer: RespecScreenPresenter
            "IronGrind.LevelingSystem.IRespecRequestSender",     // consumer: RespecScreenPresenter
            "IronGrind.LevelingSystem.ILevelingEventBroadcaster", // consumer: LevelUpOverlayPresenter
            "IronGrind.LevelingSystem.LevelUpEventArgs",          // consumer: LevelUpOverlayPresenter
            "IronGrind.LevelingSystem.XpThresholdTable",          // consumer: PlayerResourceClusterPresenter (XP bar; receives Values as a list — wiring by the client composition root is planned)
        };

        /// <summary>
        /// Every other type in <c>IronGrind.Foundation</c> on the day the test was written, grouped by system
        /// in ADR-012's move order. This list only shrinks: each move story deletes its system's entries, and
        /// the test fails if an entry names a type that is no longer in Foundation. When empty, remove it.
        /// </summary>
        internal static readonly string[] NotYetMovedList =
        {
            // ---- Networking ----
            "IronGrind.Networking.DamageType",
            "IronGrind.Networking.DisconnectReason",
            "IronGrind.Networking.DisconnectType",
            "IronGrind.Networking.SessionState",
            "IronGrind.Networking.INetworkTestObserver",
            "IronGrind.Networking.PersistenceWriteReason",
            "IronGrind.Networking.IServerCrashInjector",
            "IronGrind.Networking.CrashStep",
            "IronGrind.Networking.ITransportFaultInjector",
            "IronGrind.Networking.IZoneTestConfigurator",
            "IronGrind.Networking.NetworkTestObserver",
            "IronGrind.Networking.NetworkingTestHarness",
            "IronGrind.Networking.ServerCrashInjector",
            "IronGrind.Networking.TransportFaultInjector",
            "IronGrind.Networking.ZoneTestConfigurator",
            "IronGrind.Networking.BatchHeaderCodec",
            "IronGrind.Networking.BatchSubMessageCodec",
            "IronGrind.Networking.BatchSubMessageFraming",
            "IronGrind.Networking.BuildConfiguration",
            "IronGrind.Networking.ClientBufferSet",
            "IronGrind.Networking.ClientEntityMessageEnvelope",
            "IronGrind.Networking.ConnectionSequenceCounter",
            "IronGrind.Networking.CycleBroadcastPacketWriter",
            "IronGrind.Networking.CycleTimerBroadcast",
            "IronGrind.Networking.CycleTimerInterpolator",
            "IronGrind.Networking.DamageEvent",
            "IronGrind.Networking.DesignPillar",
            "IronGrind.Networking.EntityHealthUpdate",
            "IronGrind.Networking.EntityPositionUpdate",
            "IronGrind.Networking.GoldSyncEvent",
            "IronGrind.Networking.GoldSyncEventForcedDelivery",
            "IronGrind.Networking.GoldSyncForcedDeliveryCodec",
            "IronGrind.Networking.GoldSyncForcedDeliveryTracker",
            "IronGrind.Networking.HeartbeatActivityTracker",
            "IronGrind.Networking.HeartbeatMessage",
            "IronGrind.Networking.MessageDeliveryContext",
            "IronGrind.Networking.MessageDirection",
            "IronGrind.Networking.MessageEnvelopeCodec",
            "IronGrind.Networking.MessageRoutingEntry",
            "IronGrind.Networking.MessageRoutingRegistry",
            "IronGrind.Networking.MessageRoutingResult",
            "IronGrind.Networking.NetworkChannel",
            "IronGrind.Networking.PartyMemberHealthUpdate",
            "IronGrind.Networking.PendingSchemaDispatchException",
            "IronGrind.Networking.PendingSubMessage",
            "IronGrind.Networking.PositionPacketWriter",
            "IronGrind.Networking.PriorityPathQueue`1",
            "IronGrind.Networking.QueuedMessage`1",
            "IronGrind.Networking.RUBatchCategory",
            "IronGrind.Networking.RUBatchWriter",
            "IronGrind.Networking.RelevanceFilter",
            "IronGrind.Networking.SelfDamageEvent",
            "IronGrind.Networking.SelfDamageEventCodec",
            "IronGrind.Networking.SelfDamageEventDispatcher",
            "IronGrind.Networking.SelfDamageRecipientGuard",
            "IronGrind.Networking.SelfDamageSuppressionGate",
            "IronGrind.Networking.ServerMessageEnvelope",
            "IronGrind.Networking.SetTarget",
            "IronGrind.Networking.SetTargetCodec",
            "IronGrind.Networking.SetTargetOutcome",
            "IronGrind.Networking.StaleDiscardComparer",
            "IronGrind.Networking.TargetSlotTracker",
            "IronGrind.Networking.WireEnumCodec",
            "IronGrind.Networking.WireFixedPointCodec",
            "IronGrind.Networking.WireIdCodec",
            "IronGrind.Networking.ZoneBufferPool",
            "IronGrind.Networking.ZoneSnapshotReassemblyTracker",
            "IronGrind.Networking.ZoneState",

            // ---- Currency ----
            "IronGrind.Currency.CurrencySystem",
            "IronGrind.Currency.GoldMutationError",
            "IronGrind.Currency.GoldMutationResult",
            "IronGrind.Currency.GoldSyncEventArgs",
            "IronGrind.Currency.ICurrencyService",

            // ---- Character Stats ----
            "IronGrind.CharacterStats.BuffID",
            "IronGrind.CharacterStats.BuffModifierEntry",
            "IronGrind.CharacterStats.CharacterStats",
            "IronGrind.CharacterStats.EquipmentModifierEntry",
            "IronGrind.CharacterStats.ILevelingService",
            "IronGrind.CharacterStats.StatSchema",
        };

        /// <summary>
        /// Namespaces whose every type is server-only, nested namespaces included: none may be defined in <c>IronGrind.Foundation</c>
        /// or <c>IronGrind.Client</c>, and each must have at least one type in <c>IronGrind.ServerLogic</c>.
        /// A move story adds its namespace here only if the whole namespace moved. Namespaces that keep
        /// shared types in <c>Foundation</c> (Networking, Currency, Character Stats, Leveling,
        /// Inventory) are not listed.
        /// </summary>
        internal static readonly string[] ServerOnlyNamespaces =
        {
            "IronGrind.DamageCalculation", // Damage Calculation Story 006
            "IronGrind.LootTableSystem",   // Loot Table Story 014
            "IronGrind.EnhancementSystem", // Enhancement Story 012
            "IronGrind.NpcInteraction",    // Enhancement Story 013
        };
    }
}
