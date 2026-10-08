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

            // Networking (ADR-012 Decision 4): message schemas, the codecs that read and write them, wire enums,
            // and logic both sides or the client run. "planned" marks a consumer that is not built yet.
            // Networking: wire message schemas
            "IronGrind.Networking.ServerMessageEnvelope",       // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.ClientEntityMessageEnvelope", // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.DamageEvent",                 // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.SelfDamageEvent",             // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.GoldSyncEvent",               // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.GoldSyncEventForcedDelivery", // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.CycleTimerBroadcast",         // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.EntityPositionUpdate",        // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.EntityHealthUpdate",          // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.PartyMemberHealthUpdate",     // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.HeartbeatMessage",            // consumer (planned): client message handlers (wire schema)
            "IronGrind.Networking.SetTarget",                   // consumer (planned): client message handlers (wire schema)
            // Networking: codecs
            "IronGrind.Networking.MessageEnvelopeCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchHeaderCodec",            // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchSubMessageCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchSubMessageFraming",      // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.GoldSyncForcedDeliveryCodec", // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.SelfDamageEventCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.SetTargetCodec",              // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.WireEnumCodec",               // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.WireFixedPointCodec",         // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.WireIdCodec",                 // consumer (planned): client message read and write path (codec)
            // Networking: wire enums
            "IronGrind.Networking.DamageType",             // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.DisconnectReason",       // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.DisconnectType",         // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.MessageDirection",       // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.MessageDeliveryContext", // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.NetworkChannel",         // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.DesignPillar",           // consumer (planned): client message handlers (wire enum)
            "IronGrind.Networking.RUBatchCategory",        // consumer (planned): client message handlers (wire enum)
            // Networking: routing table
            "IronGrind.Networking.MessageRoutingRegistry",         // consumer (planned): client send and receive path (every call site consults the registry)
            "IronGrind.Networking.MessageRoutingEntry",            // consumer (planned): client send and receive path (every call site consults the registry)
            "IronGrind.Networking.MessageRoutingResult",           // consumer (planned): client send and receive path (every call site consults the registry)
            "IronGrind.Networking.PendingSchemaDispatchException", // consumer (planned): client send and receive path (every call site consults the registry)
            "IronGrind.Networking.BuildConfiguration",             // consumer (planned): client send and receive path (every call site consults the registry)
            // Networking: rules both sides apply
            "IronGrind.Networking.ConnectionSequenceCounter", // consumer (planned): client send path (per-connection sequence numbers)
            "IronGrind.Networking.StaleDiscardComparer",      // consumer (planned): client receive path (stale-message discard)
            // Networking: logic the client runs
            "IronGrind.Networking.CycleTimerInterpolator",        // consumer (planned): client cycle-timer display (its doc comment: client-render-oriented interpolation)
            "IronGrind.Networking.HeartbeatActivityTracker",      // consumer (planned): client heartbeat sender (networking-wire-protocol.md CR-NET-7.10: heartbeats are client to server)
            "IronGrind.Networking.SelfDamageRecipientGuard",      // consumer (planned): client self-damage handling (its doc comment: client-side recipient check)
            "IronGrind.Networking.SelfDamageSuppressionGate",     // consumer (planned): client damage-number display (its doc comment: client-side suppression)
            "IronGrind.Networking.ZoneSnapshotReassemblyTracker", // consumer (planned): client zone-snapshot reassembly (networking-session.md: the client reassembles and gives up after the retransmit limit)
            // Networking: test observer
            "IronGrind.Networking.INetworkTestObserver",   // consumer: shared codecs take the observer as an optional parameter (tests and development builds only)
            "IronGrind.Networking.PersistenceWriteReason", // consumer: INetworkTestObserver signatures
            "IronGrind.Networking.SessionState",           // consumer: INetworkTestObserver signatures
            "IronGrind.Networking.ZoneState",              // consumer: INetworkTestObserver signatures
        };

        /// <summary>
        /// Every other type in <c>IronGrind.Foundation</c> on the day the test was written, grouped by system
        /// in ADR-012's move order. This list only shrinks: each move story deletes its system's entries, and
        /// the test fails if an entry names a type that is no longer in Foundation. When empty, remove it.
        /// </summary>
        internal static readonly string[] NotYetMovedList =
        {
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
