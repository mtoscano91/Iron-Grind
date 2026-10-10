namespace IronGrind.Tests.EditMode.Architecture
{
    /// <summary>
    /// Data for <see cref="AssemblyBoundary_Tests"/> (ADR-012 Decision 6, check 1). Every top-level type
    /// defined in <c>IronGrind.Foundation</c> must be on <see cref="SharedAllowList"/>, as a full
    /// reflection name (<c>Namespace.TypeName</c>; generic types carry their arity suffix, for example
    /// <c>`1</c>). Nested and compiler-generated types are matched through their declaring type.
    /// The not-yet-moved list was removed when the last system moved (Character Stats Story 010,
    /// 2026-10-08). A type is added to <c>Foundation</c> by adding an entry with its client consumer named;
    /// move stories used to edit this file, and new shared types still do.
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

            // Enhancement: the result code is a wire enum decoded by clients (Enhancement Story 010).
            "IronGrind.EnhancementSystem.EnhancementResultCode", // consumer (planned): Enhancement UI epic (wire enum, EnhancementAttemptResult and EnhancementPreviewRejected messages)

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

            // Character Stats: client read model (ADR-012 Decision 7).
            "IronGrind.CharacterStats.ILocalPlayerStatsView",         // consumer: PlayerResourceClusterPresenter, RespecScreenPresenter
            "IronGrind.CharacterStats.LocalPlayerStatChangedHandler", // consumer: PlayerResourceClusterPresenter

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
            "IronGrind.Networking.EnhancementAttemptRequest",       // consumer (planned): Enhancement UI epic, confirm-enhancement request sender (wire schema)
            "IronGrind.Networking.EnhancementRequestReceived",      // consumer (planned): Enhancement UI epic, acknowledgment handler (wire schema)
            "IronGrind.Networking.EnhancementAttemptResultMessage", // consumer (planned): Enhancement UI epic, result handler (wire schema; wire name EnhancementAttemptResult)
            "IronGrind.Networking.CancelEnhancement",               // consumer (planned): Enhancement UI epic, selection cancel sender (wire schema)
            "IronGrind.Networking.EnhancementPreviewRequest",       // consumer (planned): Enhancement UI epic, preview request sender (wire schema)
            "IronGrind.Networking.EnhancementStateUpdate",          // consumer (planned): Enhancement UI epic, probability display (wire schema)
            "IronGrind.Networking.EnhancementPreviewRejected",      // consumer (planned): Enhancement UI epic, selection-cleared message (wire schema)
            "IronGrind.Networking.ServerBroadcast_Enhancement9",    // consumer (planned): Enhancement UI epic, +9 announcement display (wire schema)
            "IronGrind.Networking.OpenNPCInteraction",              // consumer (planned): NPC shop and enhancement screens, session open sender (wire schema)
            "IronGrind.Networking.CloseNPCInteraction",             // consumer (planned): NPC shop and enhancement screens, session close sender (wire schema)
            "IronGrind.Networking.NPCInteractionOpened",            // consumer (planned): NPC shop and enhancement screens, session handler (wire schema)
            "IronGrind.Networking.RejectedNotInTownHub",            // consumer (planned): NPC shop screen, rejection handler (wire schema)
            // Networking: codecs
            "IronGrind.Networking.MessageEnvelopeCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchHeaderCodec",            // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchSubMessageCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.BatchSubMessageFraming",      // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.GoldSyncForcedDeliveryCodec", // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.SelfDamageEventCodec",        // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.SetTargetCodec",              // consumer (planned): client message read and write path (codec)
            "IronGrind.Networking.EnhancementAttemptRequestCodec",       // consumer (planned): Enhancement UI epic, request writer (codec)
            "IronGrind.Networking.EnhancementRequestReceivedCodec",      // consumer (planned): Enhancement UI epic, acknowledgment reader (codec)
            "IronGrind.Networking.EnhancementAttemptResultMessageCodec", // consumer (planned): Enhancement UI epic, result reader (codec)
            "IronGrind.Networking.CancelEnhancementCodec",               // consumer (planned): Enhancement UI epic, cancel writer (codec)
            "IronGrind.Networking.EnhancementPreviewRequestCodec",       // consumer (planned): Enhancement UI epic, preview request writer (codec)
            "IronGrind.Networking.EnhancementStateUpdateCodec",          // consumer (planned): Enhancement UI epic, probability reader (codec)
            "IronGrind.Networking.EnhancementPreviewRejectedCodec",      // consumer (planned): Enhancement UI epic, preview rejection reader (codec)
            "IronGrind.Networking.ServerBroadcast_Enhancement9Codec",    // consumer (planned): Enhancement UI epic, +9 announcement reader (codec)
            "IronGrind.Networking.OpenNPCInteractionCodec",              // consumer (planned): NPC shop and enhancement screens, session open writer (codec)
            "IronGrind.Networking.CloseNPCInteractionCodec",             // consumer (planned): NPC shop and enhancement screens, session close writer (codec)
            "IronGrind.Networking.NPCInteractionOpenedCodec",            // consumer (planned): NPC shop and enhancement screens, session reader (codec)
            "IronGrind.Networking.RejectedNotInTownHubCodec",            // consumer (planned): NPC shop screen, rejection reader (codec)
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
            "IronGrind.NpcInteraction",    // Enhancement Story 013
            "IronGrind.Randomness",        // Damage Calculation Story 003 (ADR-013)
        };
    }
}
