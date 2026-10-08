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
            // Ids used by client screens and wire messages.
            "IronGrind.CharacterStats.EntityID",   // consumer: HUD presenters (target and party frames)
            "IronGrind.CharacterStats.ItemID",     // consumer: inventory and item tooltip screens
            "IronGrind.CharacterStats.StatID",     // consumer: stat panel, LevelingFormulaPreview
            "IronGrind.Currency.CharacterID",      // consumer: HUD and gold display (wire id)
            "IronGrind.Currency.GoldTransactionReason", // consumer: gold sync event display (wire enum)

            // Item Database: static definition data both sides load (whole folder is shared).
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

            // Class definitions shown by the respec flow.
            "IronGrind.LevelingSystem.ClassDefinition", // consumer: RespecScreenPresenter, LevelingFormulaPreview
            "IronGrind.LevelingSystem.IClassRegistry",  // consumer: RespecScreenPresenter, LevelingFormulaPreview
            "IronGrind.LevelingSystem.ClassRegistry",   // consumer: RespecScreenPresenter, LevelingFormulaPreview
        };

        /// <summary>
        /// Every other type in <c>IronGrind.Foundation</c> on the day the test was written, grouped by system
        /// in ADR-012's move order. This list only shrinks: each move story deletes its system's entries, and
        /// the test fails if an entry names a type that is no longer in Foundation. When empty, remove it.
        /// </summary>
        internal static readonly string[] NotYetMovedList =
        {
            // ---- Loot Table ----
            "IronGrind.LootTableSystem.AuctionResolvedEventArgs",
            "IronGrind.LootTableSystem.BagFullPickupBlockedEventArgs",
            "IronGrind.LootTableSystem.DropTier",
            "IronGrind.LootTableSystem.GroundItem",
            "IronGrind.LootTableSystem.GroundItemAssignedEventArgs",
            "IronGrind.LootTableSystem.GroundItemDespawnedEventArgs",
            "IronGrind.LootTableSystem.GroundItemExpiryWarningEventArgs",
            "IronGrind.LootTableSystem.GroundItemID",
            "IronGrind.LootTableSystem.GroundItemService",
            "IronGrind.LootTableSystem.GroundItemSpawnedEventArgs",
            "IronGrind.LootTableSystem.GroundItemState",
            "IronGrind.LootTableSystem.ICharacterPositionProvider",
            "IronGrind.LootTableSystem.IGroundItemService",
            "IronGrind.LootTableSystem.ILootAuctionService",
            "IronGrind.LootTableSystem.ILootDropSink",
            "IronGrind.LootTableSystem.ILootTableService",
            "IronGrind.LootTableSystem.IMobInfoProvider",
            "IronGrind.LootTableSystem.IPartyService",
            "IronGrind.LootTableSystem.LootAuctionService",
            "IronGrind.LootTableSystem.LootBidResult",
            "IronGrind.LootTableSystem.LootBidUpdateEventArgs",
            "IronGrind.LootTableSystem.LootDropDistributor",
            "IronGrind.LootTableSystem.LootDropRoller",
            "IronGrind.LootTableSystem.LootEquipmentCache",
            "IronGrind.LootTableSystem.LootRandomFactory",
            "IronGrind.LootTableSystem.LootTableConstants",
            "IronGrind.LootTableSystem.LootTableDefinition",
            "IronGrind.LootTableSystem.LootTableEntry",
            "IronGrind.LootTableSystem.LootTableRegistry",
            "IronGrind.LootTableSystem.LootTableService",
            "IronGrind.LootTableSystem.LootTableValidationIssue",
            "IronGrind.LootTableSystem.LootTableValidator",
            "IronGrind.LootTableSystem.LootTeardownCoordinator",
            "IronGrind.LootTableSystem.MobInfo",
            "IronGrind.LootTableSystem.MobTypeID",
            "IronGrind.LootTableSystem.PartyID",
            "IronGrind.LootTableSystem.PartyTagTracker",

            // ---- Enhancement ----
            "IronGrind.EnhancementSystem.EnhancementAttemptResult",
            "IronGrind.EnhancementSystem.EnhancementAttemptStart",
            "IronGrind.EnhancementSystem.EnhancementAttemptValidation",
            "IronGrind.EnhancementSystem.EnhancementBonusProvider",
            "IronGrind.EnhancementSystem.EnhancementBroadcastEventArgs",
            "IronGrind.EnhancementSystem.EnhancementConfig",
            "IronGrind.EnhancementSystem.EnhancementConstants",
            "IronGrind.EnhancementSystem.EnhancementDestructionEventArgs",
            "IronGrind.EnhancementSystem.EnhancementOutcome",
            "IronGrind.EnhancementSystem.EnhancementResultCode",
            "IronGrind.EnhancementSystem.EnhancementService",
            "IronGrind.EnhancementSystem.EnhancementSuccessEventArgs",
            "IronGrind.EnhancementSystem.IEnhancementBonusProvider",
            "IronGrind.EnhancementSystem.PrestigeBand",

            // ---- NPC Interaction ----
            "IronGrind.NpcInteraction.INpcInteractionSessions",
            "IronGrind.NpcInteraction.ITownHubQuery",
            "IronGrind.NpcInteraction.NpcInteractionOpenResult",
            "IronGrind.NpcInteraction.NpcInteractionSessionTracker",

            // ---- Inventory ----
            "IronGrind.InventorySystem.ConsumeItemFailReason",
            "IronGrind.InventorySystem.ConsumeItemResult",
            "IronGrind.InventorySystem.DiscardFailReason",
            "IronGrind.InventorySystem.DiscardResult",
            "IronGrind.InventorySystem.IInventoryService",
            "IronGrind.InventorySystem.InventoryChangedEventArgs",
            "IronGrind.InventorySystem.InventoryConstants",
            "IronGrind.InventorySystem.InventoryFullEventArgs",
            "IronGrind.InventorySystem.InventoryService",
            "IronGrind.InventorySystem.InventorySlot",
            "IronGrind.InventorySystem.InventorySnapshot",
            "IronGrind.InventorySystem.InventorySnapshotEntry",
            "IronGrind.InventorySystem.MoveFailReason",
            "IronGrind.InventorySystem.MoveItemInResult",
            "IronGrind.InventorySystem.MoveItemOutCode",
            "IronGrind.InventorySystem.MoveItemOutResult",
            "IronGrind.InventorySystem.MoveResult",
            "IronGrind.InventorySystem.PickupFailReason",
            "IronGrind.InventorySystem.PickupResult",
            "IronGrind.InventorySystem.SellItemFailReason",
            "IronGrind.InventorySystem.SellItemResult",
            "IronGrind.InventorySystem.SlotChange",

            // ---- Leveling ----
            "IronGrind.LevelingSystem.AllocateFreePointResult",
            "IronGrind.LevelingSystem.IItemReservation",
            "IronGrind.LevelingSystem.ILevelingEventBroadcaster",
            "IronGrind.LevelingSystem.LevelUpEventArgs",
            "IronGrind.LevelingSystem.LevelingService",
            "IronGrind.LevelingSystem.LevelingStateSnapshot",
            "IronGrind.LevelingSystem.RespecTwoPhaseCommitCoordinator",
            "IronGrind.LevelingSystem.XpThresholdTable",

            // ---- Networking ----
            "IronGrind.Networking.CommitBeforeBroadcastResult",
            "IronGrind.Networking.CommitBeforeBroadcastSequencer",
            "IronGrind.Networking.ConnectionStateMachine",
            "IronGrind.Networking.PendingPurchaseRecord",
            "IronGrind.Networking.RespecReservationStatus",
            "IronGrind.Networking.SessionHandshakeData",
            "IronGrind.Networking.DamageType",
            "IronGrind.Networking.DisconnectReason",
            "IronGrind.Networking.DisconnectType",
            "IronGrind.Networking.EnhancementRequestDedupResult",
            "IronGrind.Networking.EnhancementRequestDeduplicator",
            "IronGrind.Networking.GhostCleanupSequencer",
            "IronGrind.Networking.GhostDismissalCoordinator",
            "IronGrind.Networking.GhostEntityTracker",
            "IronGrind.Networking.GhostXpPoolTracker",
            "IronGrind.Networking.MobDeTargetingCoordinator",
            "IronGrind.Networking.PartyDisbandCoordinator",
            "IronGrind.Networking.PreDisconnectSnapshot",
            "IronGrind.Networking.PreDisconnectSnapshotWal",
            "IronGrind.Networking.GhostZoneCrashSession",
            "IronGrind.Networking.ZoneCrashCleanupHandler",
            "IronGrind.Networking.IIrreversibleOutcomeCoordinator",
            "IronGrind.Networking.IrreversibleOutcomeBeginResult",
            "IronGrind.Networking.IrreversibleOutcomeCoordinator",
            "IronGrind.Networking.IrreversibleWriteFailureProtocol",
            "IronGrind.Networking.CharacterMutationGate",
            "IronGrind.Networking.ICharacterMutationGate",
            "IronGrind.Networking.LastBeatServerTickTracker",
            "IronGrind.Networking.OwlThresholdHysteresisTracker",
            "IronGrind.Networking.OwlWrapCorrectionFormula",
            "IronGrind.Networking.SkillGraceWindowResult",
            "IronGrind.Networking.CrossCuttingRpcGuardChain",
            "IronGrind.Networking.InboundRpcDescriptor",
            "IronGrind.Networking.RpcGuardResult",
            "IronGrind.Networking.RpcTypeTag",
            "IronGrind.Networking.SessionState",
            "IronGrind.Networking.SessionTokenStore",
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
            "IronGrind.Networking.ITickCompletionQueue",
            "IronGrind.Networking.TickCompletionConstants",
            "IronGrind.Networking.TickCompletionQueue",
            "IronGrind.Networking.TickTaskStatus",
            "IronGrind.Networking.TickTaskResult`1",
            "IronGrind.Networking.ServerTickLoop",
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
            "IronGrind.Networking.ZoneSessionStateMachine",
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
    }
}
