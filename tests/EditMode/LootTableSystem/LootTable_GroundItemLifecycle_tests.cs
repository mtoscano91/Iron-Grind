using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.ItemDatabase;
using IronGrind.LootTableSystem;
using IronGrind.Tests.EditMode.ItemDatabase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.LootTableSystem
{
    /// <summary>
    /// EditMode unit tests for Loot Table Story 005: ground item lifecycle and TTL despawn
    /// (design/gdd/loot-table-system.md CR-LT-12, GroundItem state table, AC-LT-16 common half;
    /// ADR-010 Tier 2 events).
    /// </summary>
    [TestFixture]
    internal sealed class LootTable_GroundItemLifecycle_Tests
    {
        private const uint SPAWN_TICK = 100u;
        private const uint EXPIRY_TICK = SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS;
        private const uint TICK_BEFORE_EXPIRY = EXPIRY_TICK - 1u;
        private const uint TICK_AFTER_EXPIRY = EXPIRY_TICK + 1u;
        private const uint TICK_LONG_AFTER_EXPIRY = EXPIRY_TICK + 500u;
        private const uint NEXT_TICK = SPAWN_TICK + 1u;

        private const uint BRONZE_ID = 1001u;
        private const uint STEEL_ID = 1003u;
        private const uint UNCACHED_ID = 9001u;
        private const uint RAW_ITEM = 101u;
        private const uint RAW_CHAR = 11u;
        private const uint RAW_CHAR_OTHER = 12u;
        private const uint RAW_MOB = 500u;
        private const uint RAW_PARTY = 1u;
        private const uint RAW_MOB_TYPE = 7u;
        private const int MOB_MAX_HP = 300;
        private const uint KILLING_DAMAGE = 300u;
        private const int GOLD_MIN = 4;
        private const int GOLD_MAX = 8;
        private const float DROP_CHANCE = 0.25f;
        private const double MISSING_DRAW = 0.5;
        private const double HITTING_DRAW = 0.0;
        private const int NO_TIER_SHIFT = 0;
        private const int PRNG_SEED = 4242;

        private const uint LATER_SPAWN_TICK = SPAWN_TICK + 100u;
        private const uint LATE_FIRST_TICK = SPAWN_TICK + 5u;
        private const uint TICK_BEFORE_SPAWN = SPAWN_TICK - 1u;
        private const uint WRAP_SPAWN_TICK = uint.MaxValue - 100u;
        private const uint WRAPPED_EXPIRY_TICK = unchecked(WRAP_SPAWN_TICK + (uint)LootTableConstants.GROUND_ITEM_TTL_TICKS);
        private const uint TICK_BEFORE_WRAPPED_EXPIRY = WRAPPED_EXPIRY_TICK - 1u;
        private const string HANDLER_FAILURE = "subscriber failure";

        private static readonly ItemID ItemA = new ItemID(RAW_ITEM);
        private static readonly CharacterID CharA = new CharacterID(RAW_CHAR);
        private static readonly CharacterID CharB = new CharacterID(RAW_CHAR_OTHER);
        private static readonly EntityID Mob = new EntityID(RAW_MOB);
        private static readonly PartyID Party = new PartyID(RAW_PARTY);
        private static readonly Vector3 SpawnPosition = new Vector3(1f, 2f, 3f);

        private readonly List<ItemDefinition> _created = new List<ItemDefinition>();
        private int _errorCount;

        [SetUp]
        public void SetUp()
        {
            _errorCount = 0;
            Application.logMessageReceived += CountError;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CountError;
            foreach (ItemDefinition def in _created)
            {
                if (def != null)
                {
                    UnityEngine.Object.DestroyImmediate(def);
                }
            }
            _created.Clear();
        }

        // LogAssert.Expect only proves an error arrived; this counts them so "exactly one" can be asserted.
        private void CountError(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error)
            {
                _errorCount++;
            }
        }

        // -----------------------------------------------------------------------
        // Helpers and fakes
        // -----------------------------------------------------------------------

        private sealed class FakeItemDatabase : IItemDatabase
        {
            private readonly List<ItemDefinition> _equipment;

            public FakeItemDatabase(List<ItemDefinition> equipment)
            {
                _equipment = equipment;
            }

            public bool IsReady => true;

            public event Action OnDatabaseReady
            {
                add { }
                remove { }
            }

            public ItemDefinition GetItem(ItemID id) => null;

            public bool TryGetItem(ItemID id, out ItemDefinition item)
            {
                item = null;
                return false;
            }

            public IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category)
            {
                return category == ItemCategory.Equipment ? _equipment : new List<ItemDefinition>();
            }
        }

        private sealed class ScriptedRandom : System.Random
        {
            private readonly double _draw;

            public ScriptedRandom(double draw) : base(PRNG_SEED)
            {
                _draw = draw;
            }

            public override double NextDouble() => _draw;
        }

        private sealed class StubPartyService : IPartyService
        {
            public PartyID GetPartyID(CharacterID characterId) => Party;

            public IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId) => new List<CharacterID> { CharA };

            public CharacterID GetMemberAtIndex(PartyID partyId, int index) => CharacterID.Invalid;

            public int GetRrNextIndex(PartyID partyId) => 0;

            public void AdvanceRrNextIndex(PartyID partyId) { }

            public bool IsMemberConnected(CharacterID characterId) => true;
        }

        private sealed class StubMobInfoProvider : IMobInfoProvider
        {
            public bool TryGetMob(EntityID mobEntityId, out MobInfo info)
            {
                info = new MobInfo(new MobTypeID(RAW_MOB_TYPE), MOB_MAX_HP, SpawnPosition);
                return mobEntityId == Mob;
            }
        }

        private sealed class NullCurrencyService : ICurrencyService
        {
            public event Action<GoldSyncEventArgs> OnGoldSync
            {
                add { }
                remove { }
            }

            public void RegisterCharacter(CharacterID charId, uint initialBalance) { }

            public GoldMutationResult AddGold(CharacterID charId, uint amount, GoldTransactionReason reason)
            {
                return new GoldMutationResult(true, amount, 0u, GoldMutationError.None);
            }

            public uint GetBalance(CharacterID charId) => 0u;

            public GoldMutationResult TrySpendGold(CharacterID charId, uint cost, GoldTransactionReason reason)
            {
                return new GoldMutationResult(false, 0u, 0u, GoldMutationError.NotImplemented);
            }

            public GoldMutationResult TransferGold(CharacterID fromId, CharacterID toId, uint amount)
            {
                return new GoldMutationResult(false, 0u, 0u, GoldMutationError.NotImplemented);
            }
        }

        // Stands in for Story 006: forwards every resolved drop to Spawn with a fixed assignee.
        private sealed class ForwardingSink : ILootDropSink
        {
            private readonly GroundItemService _service;

            public ForwardingSink(GroundItemService service)
            {
                _service = service;
            }

            public void OnDropsResolved(EntityID mobEntityId, PartyID winningParty, IReadOnlyList<ItemID> drops, Vector3 position)
            {
                for (int i = 0; i < drops.Count; i++)
                {
                    _service.Spawn(drops[i], position, CharA, SPAWN_TICK);
                }
            }
        }

        private sealed class EventLog
        {
            public readonly List<GroundItemSpawnedEventArgs> Spawned = new List<GroundItemSpawnedEventArgs>();
            public readonly List<GroundItemDespawnedEventArgs> Despawned = new List<GroundItemDespawnedEventArgs>();

            public EventLog(GroundItemService service)
            {
                service.OnGroundItemSpawned += Spawned.Add;
                service.OnGroundItemDespawned += Despawned.Add;
            }
        }

        private ItemDefinition BuildEquipment(uint id, GearTier tier)
        {
            ItemDefinition def = ItemDefinitionBuilder.Build(
                id,
                "Item" + id,
                ItemCategory.Equipment,
                equipmentData: EquipmentData.CreateForTesting(GearSlot.Weapon, tier));
            _created.Add(def);
            return def;
        }

        private LootEquipmentCache BuildCache()
        {
            var equipment = new List<ItemDefinition>
            {
                BuildEquipment(BRONZE_ID, GearTier.Bronze),
                BuildEquipment(STEEL_ID, GearTier.Steel),
            };
            return new LootEquipmentCache(new FakeItemDatabase(equipment));
        }

        private GroundItemService BuildService()
        {
            return new GroundItemService(BuildCache(), new RecordingInventoryService(), new SettablePositionProvider(), new EmptyItemDatabase());
        }

        // -----------------------------------------------------------------------
        // AC-LT-16 (common half)
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_ItemSpawnedAt100_DespawnsOnlyAtExpiryTickAndOnlyOnce()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            service.Tick(SPAWN_TICK);
            service.Tick(NEXT_TICK);

            // Act / Assert: still alive one tick before expiry
            service.Tick(TICK_BEFORE_EXPIRY);
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem before));
            Assert.AreEqual(GroundItemState.Assigned, before.State);
            Assert.AreEqual(0, log.Despawned.Count);

            // Act: expiry tick reached
            service.Tick(EXPIRY_TICK);

            // Assert
            Assert.AreEqual(1, log.Despawned.Count);
            Assert.AreEqual(id, log.Despawned[0].GroundItemId);
            Assert.IsFalse(service.TryGetGroundItem(id, out _));

            // Past expiry: no second event
            service.Tick(TICK_AFTER_EXPIRY);
            service.Tick(TICK_LONG_AFTER_EXPIRY);
            Assert.AreEqual(1, log.Despawned.Count);
        }

        // -----------------------------------------------------------------------
        // CR-LT-12
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_AtTick100_ExpiryTickIsSpawnTickPlusTtl()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);

            // Act
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Assert
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(EXPIRY_TICK, item.ExpiryTick);
            Assert.AreEqual(SPAWN_TICK, item.SpawnTick);
            Assert.AreEqual(LootTableConstants.GROUND_ITEM_TTL_PAUSE_CAP_TICKS, item.PauseBudgetRemaining);
            Assert.AreEqual(EXPIRY_TICK, log.Spawned[0].ExpiryTick);
        }

        // -----------------------------------------------------------------------
        // Spawning lasts one tick / spawn event timing
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_Returns_HasRaisedSpawnedEventOnceWithFullPayloadBeforeAnyTick()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);

            // Act
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Assert
            Assert.AreEqual(1, log.Spawned.Count);
            GroundItemSpawnedEventArgs e = log.Spawned[0];
            Assert.AreEqual(id, e.GroundItemId);
            Assert.AreEqual(ItemA, e.ItemId);
            Assert.AreEqual(SpawnPosition, e.Position);
            Assert.AreEqual(CharA, e.AssignedTo);
            Assert.IsFalse(e.IsAuction);
            Assert.AreEqual(EXPIRY_TICK, e.ExpiryTick);
        }

        [Test]
        public void Tick_AfterSpawn_StaysSpawningOnSpawnTickThenAssignedAndNeverRaisesSpawnedAgain()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Assert: Spawning right after Spawn
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem afterSpawn));
            Assert.AreEqual(GroundItemState.Spawning, afterSpawn.State);

            // Act / Assert: Tick on the spawn tick keeps it Spawning
            service.Tick(SPAWN_TICK);
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem afterSameTick));
            Assert.AreEqual(GroundItemState.Spawning, afterSameTick.State);

            // Act / Assert: the next tick moves it to Assigned
            service.Tick(NEXT_TICK);
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem afterNextTick));
            Assert.AreEqual(GroundItemState.Assigned, afterNextTick.State);
            Assert.AreEqual(1, log.Spawned.Count);
        }

        // -----------------------------------------------------------------------
        // gearTier
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_CachedEquipmentItem_EventReportsItsGearTier()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);

            // Act
            service.Spawn(new ItemID(STEEL_ID), SpawnPosition, CharA, SPAWN_TICK);

            // Assert
            Assert.AreEqual(GearTier.Steel, log.Spawned[0].GearTier);
        }

        [Test]
        public void Spawn_ItemNotInEquipmentCache_EventReportsGearTierNone()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);

            // Act
            service.Spawn(new ItemID(UNCACHED_ID), SpawnPosition, CharA, SPAWN_TICK);

            // Assert
            Assert.AreEqual(GearTier.None, log.Spawned[0].GearTier);
        }

        // -----------------------------------------------------------------------
        // Terminal state
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_AfterDespawn_RaisesNothingAndRecordIsGone()
        {
            // Arrange
            GroundItemService service = BuildService();
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            service.Tick(EXPIRY_TICK);
            var log = new EventLog(service);

            // Act
            service.Tick(TICK_AFTER_EXPIRY);
            service.Tick(TICK_LONG_AFTER_EXPIRY);

            // Assert
            Assert.AreEqual(0, log.Despawned.Count);
            Assert.AreEqual(0, log.Spawned.Count);
            Assert.IsFalse(service.TryGetGroundItem(id, out _));
        }

        // -----------------------------------------------------------------------
        // Zero items
        // -----------------------------------------------------------------------

        // A real LootTableService over a one-entry table (DROP_CHANCE), with the mob already tagged.
        // The draw decides the roll: draw < DROP_CHANCE drops the item.
        private static LootTableService BuildTaggedLootService(GroundItemService ground, double draw)
        {
            var table = new LootTableDefinition(new[] { new LootTableEntry(ItemA, DROP_CHANCE) }, GOLD_MIN, GOLD_MAX);
            var tables = new List<KeyValuePair<MobTypeID, LootTableDefinition>>
            {
                new KeyValuePair<MobTypeID, LootTableDefinition>(new MobTypeID(RAW_MOB_TYPE), table),
            };
            Assert.IsTrue(LootTableRegistry.TryCreate(tables, new EmptyItemDatabase(), LootTableConstants.ALLOW_ENHANCEMENT_SCROLL_DROPS, out LootTableRegistry registry, out _));
            var parties = new StubPartyService();
            var mobs = new StubMobInfoProvider();
            var tracker = new PartyTagTracker(parties, mobs, () => SPAWN_TICK);
            var loot = new LootTableService(
                registry, tracker, parties, mobs, new NullCurrencyService(),
                new ScriptedRandom(draw), new ForwardingSink(ground));
            loot.RecordDamage(Mob, CharA, KILLING_DAMAGE);
            return loot;
        }

        [Test]
        public void ResolveMobDrop_EveryRollMisses_RaisesNoSpawnEvent()
        {
            // Arrange
            GroundItemService ground = BuildService();
            var log = new EventLog(ground);
            LootTableService loot = BuildTaggedLootService(ground, MISSING_DRAW);

            // Act
            loot.ResolveMobDrop(Mob, NO_TIER_SHIFT);

            // Assert
            Assert.AreEqual(0, log.Spawned.Count);
        }

        // Positive control for the test above: the same rig with a hitting draw does spawn, so the
        // zero result is caused by the missed roll and not by broken wiring.
        [Test]
        public void ResolveMobDrop_RollHits_ForwardedDropRaisesOneSpawnEvent()
        {
            // Arrange
            GroundItemService ground = BuildService();
            var log = new EventLog(ground);
            LootTableService loot = BuildTaggedLootService(ground, HITTING_DRAW);

            // Act
            loot.ResolveMobDrop(Mob, NO_TIER_SHIFT);

            // Assert
            Assert.AreEqual(1, log.Spawned.Count);
            Assert.AreEqual(ItemA, log.Spawned[0].ItemId);
            Assert.AreEqual(SpawnPosition, log.Spawned[0].Position);
        }

        // -----------------------------------------------------------------------
        // GroundItemID
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_ThreeItems_ReturnsThreeDistinctIdsNoneInvalid()
        {
            // Arrange
            GroundItemService service = BuildService();

            // Act
            GroundItemID first = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            GroundItemID second = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            GroundItemID third = service.Spawn(ItemA, SpawnPosition, CharB, SPAWN_TICK);

            // Assert
            Assert.AreNotEqual(GroundItemID.Invalid, first);
            Assert.AreNotEqual(GroundItemID.Invalid, second);
            Assert.AreNotEqual(GroundItemID.Invalid, third);
            Assert.AreNotEqual(first, second);
            Assert.AreNotEqual(first, third);
            Assert.AreNotEqual(second, third);
        }

        // -----------------------------------------------------------------------
        // Invalid arguments
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_ItemIdZero_ReturnsInvalidLogsOneErrorAndRaisesNothing()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Spawn"));

            // Act
            GroundItemID id = service.Spawn(ItemID.Invalid, SpawnPosition, CharA, SPAWN_TICK);

            // Assert
            Assert.AreEqual(GroundItemID.Invalid, id);
            Assert.AreEqual(1, _errorCount);
            Assert.AreEqual(0, log.Spawned.Count);
            Assert.IsFalse(service.TryGetGroundItem(id, out _));
            Assert.AreNotEqual(GroundItemID.Invalid, service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK));
        }

        [Test]
        public void Spawn_CharacterIdZero_ReturnsInvalidLogsOneErrorAndRaisesNothing()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Spawn"));

            // Act
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharacterID.Invalid, SPAWN_TICK);

            // Assert
            Assert.AreEqual(GroundItemID.Invalid, id);
            Assert.AreEqual(1, _errorCount);
            Assert.AreEqual(0, log.Spawned.Count);
            Assert.IsFalse(service.TryGetGroundItem(id, out _));
            Assert.AreNotEqual(GroundItemID.Invalid, service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK));
        }

        [Test]
        public void Spawn_AfterInvalidSpawn_NextIdIsDirectSuccessorOfThePreviousOne()
        {
            // Arrange
            GroundItemService service = BuildService();
            LogAssert.Expect(LogType.Error, new Regex(@"\[GroundItemService\] Spawn"));
            GroundItemID first = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Act
            service.Spawn(ItemID.Invalid, SpawnPosition, CharA, SPAWN_TICK);
            GroundItemID next = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Assert: the invalid call consumed no ID
            Assert.AreEqual(first.RawValue + 1u, next.RawValue);
        }

        // -----------------------------------------------------------------------
        // Tick boundaries (code review, 2026-10-02)
        // -----------------------------------------------------------------------

        [Test]
        public void Tick_TwoItemsWithDifferentExpiry_OnlyTheDueOneDespawns()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            GroundItemID due = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            GroundItemID later = service.Spawn(ItemA, SpawnPosition, CharB, LATER_SPAWN_TICK);

            // Act
            service.Tick(EXPIRY_TICK);

            // Assert
            Assert.AreEqual(1, log.Despawned.Count);
            Assert.AreEqual(due, log.Despawned[0].GroundItemId);
            Assert.IsFalse(service.TryGetGroundItem(due, out _));
            Assert.IsTrue(service.TryGetGroundItem(later, out GroundItem survivor));
            Assert.AreEqual(GroundItemState.Assigned, survivor.State);
        }

        [Test]
        public void Tick_ThreeItemsExpireOnSameTick_RaisesOneDespawnEventEach()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            var spawned = new HashSet<GroundItemID>
            {
                service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK),
                service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK),
                service.Spawn(ItemA, SpawnPosition, CharB, SPAWN_TICK),
            };

            // Act
            service.Tick(EXPIRY_TICK);

            // Assert: compared as a set, the order within a tick is unspecified
            var despawned = new HashSet<GroundItemID>();
            foreach (GroundItemDespawnedEventArgs e in log.Despawned)
            {
                despawned.Add(e.GroundItemId);
            }
            Assert.AreEqual(spawned.Count, log.Despawned.Count);
            Assert.IsTrue(spawned.SetEquals(despawned));
            foreach (GroundItemID id in spawned)
            {
                Assert.IsFalse(service.TryGetGroundItem(id, out _));
            }
        }

        [Test]
        public void Tick_ItemStillSpawningAtExpiry_Despawns()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Act: no tick between spawn and expiry
            service.Tick(EXPIRY_TICK);

            // Assert
            Assert.AreEqual(1, log.Despawned.Count);
            Assert.AreEqual(id, log.Despawned[0].GroundItemId);
        }

        [Test]
        public void Tick_ExpiryTickWrapsPastMaxValue_DespawnsOnlyAtTheWrappedExpiryTick()
        {
            // Arrange
            GroundItemService service = BuildService();
            var log = new EventLog(service);
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, WRAP_SPAWN_TICK);
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem spawned));
            Assert.AreEqual(WRAPPED_EXPIRY_TICK, spawned.ExpiryTick);

            // Act / Assert: alive at the last tick before the wrap and one tick before expiry
            service.Tick(uint.MaxValue);
            Assert.IsTrue(service.TryGetGroundItem(id, out _));
            service.Tick(TICK_BEFORE_WRAPPED_EXPIRY);
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem beforeExpiry));
            Assert.AreEqual(GroundItemState.Assigned, beforeExpiry.State);
            Assert.AreEqual(0, log.Despawned.Count);

            // Act / Assert: despawns at the wrapped expiry tick
            service.Tick(WRAPPED_EXPIRY_TICK);
            Assert.AreEqual(1, log.Despawned.Count);
            Assert.IsFalse(service.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_FirstTickLaterThanSpawnTickPlusOne_MovesItemToAssigned()
        {
            // Arrange
            GroundItemService service = BuildService();
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Act
            service.Tick(LATE_FIRST_TICK);

            // Assert
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Assigned, item.State);
        }

        [Test]
        public void Tick_TickOlderThanSpawnTick_ItemStaysSpawning()
        {
            // Arrange
            GroundItemService service = BuildService();
            GroundItemID id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Act
            service.Tick(TICK_BEFORE_SPAWN);

            // Assert
            Assert.IsTrue(service.TryGetGroundItem(id, out GroundItem item));
            Assert.AreEqual(GroundItemState.Spawning, item.State);
        }

        // -----------------------------------------------------------------------
        // Subscriber exceptions (code review, 2026-10-02)
        // -----------------------------------------------------------------------

        [Test]
        public void Spawn_SubscriberThrows_LogsExceptionAndStillReturnsLiveId()
        {
            // Arrange
            GroundItemService service = BuildService();
            service.OnGroundItemSpawned += _ => throw new InvalidOperationException(HANDLER_FAILURE);
            LogAssert.Expect(LogType.Exception, new Regex(HANDLER_FAILURE));

            // Act
            GroundItemID id = GroundItemID.Invalid;
            Assert.DoesNotThrow(() => id = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK));

            // Assert
            Assert.AreNotEqual(GroundItemID.Invalid, id);
            Assert.IsTrue(service.TryGetGroundItem(id, out _));
        }

        [Test]
        public void Tick_DespawnSubscriberThrows_LogsAndStillAnnouncesEveryExpiredItem()
        {
            // Arrange
            GroundItemService service = BuildService();
            GroundItemID first = service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);
            GroundItemID second = service.Spawn(ItemA, SpawnPosition, CharB, SPAWN_TICK);
            var announced = new HashSet<GroundItemID>();
            service.OnGroundItemDespawned += e =>
            {
                announced.Add(e.GroundItemId);
                throw new InvalidOperationException(HANDLER_FAILURE);
            };
            LogAssert.Expect(LogType.Exception, new Regex(HANDLER_FAILURE));
            LogAssert.Expect(LogType.Exception, new Regex(HANDLER_FAILURE));

            // Act
            Assert.DoesNotThrow(() => service.Tick(EXPIRY_TICK));

            // Assert
            Assert.IsTrue(announced.SetEquals(new[] { first, second }));
            Assert.IsFalse(service.TryGetGroundItem(first, out _));
            Assert.IsFalse(service.TryGetGroundItem(second, out _));
        }

        // -----------------------------------------------------------------------
        // Structural
        // -----------------------------------------------------------------------

        [Test]
        public void Constructor_NullEquipmentCache_Throws()
        {
            LootEquipmentCache cache = BuildCache();
            var inventory = new RecordingInventoryService();
            var positions = new SettablePositionProvider();
            var itemDatabase = new EmptyItemDatabase();

            Assert.Throws<ArgumentNullException>(() => new GroundItemService(null, inventory, positions, itemDatabase));
            Assert.Throws<ArgumentNullException>(() => new GroundItemService(cache, null, positions, itemDatabase));
            Assert.Throws<ArgumentNullException>(() => new GroundItemService(cache, inventory, null, itemDatabase));
            Assert.Throws<ArgumentNullException>(() => new GroundItemService(cache, inventory, positions, null));
        }

        [Test]
        public void Tick_DespawnHandlerSpawnsReentrantly_DoesNotThrowAndNewItemSurvives()
        {
            // Arrange
            GroundItemService service = BuildService();
            GroundItemID reentrant = GroundItemID.Invalid;
            service.OnGroundItemDespawned += _ => reentrant = service.Spawn(ItemA, SpawnPosition, CharA, EXPIRY_TICK);
            service.Spawn(ItemA, SpawnPosition, CharA, SPAWN_TICK);

            // Act
            Assert.DoesNotThrow(() => service.Tick(EXPIRY_TICK));

            // Assert
            Assert.AreNotEqual(GroundItemID.Invalid, reentrant);
            Assert.IsTrue(service.TryGetGroundItem(reentrant, out GroundItem item));
            Assert.AreEqual(GroundItemState.Spawning, item.State);
        }
    }
}
