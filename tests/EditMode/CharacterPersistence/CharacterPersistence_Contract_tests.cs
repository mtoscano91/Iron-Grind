using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.InventorySystem;
using IronGrind.ItemDatabase;
using NUnit.Framework;

namespace IronGrind.Tests.EditMode.CharacterPersistence
{
    /// <summary>Character Persistence Story 001: the contract types (enums, ids, interface, record, constants).</summary>
    [TestFixture]
    internal sealed class CharacterPersistence_Contract_Tests
    {
        private static void AssertEnum(Type enumType, params (string Name, byte Value)[] expected)
        {
            Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(enumType), enumType.Name + " underlying type");
            string[] names = Enum.GetNames(enumType);
            Assert.AreEqual(expected.Length, names.Length, enumType.Name + " member count");
            foreach ((string name, byte value) in expected)
            {
                Assert.IsTrue(Enum.IsDefined(enumType, name), enumType.Name + "." + name + " missing");
                Assert.AreEqual(value, Convert.ToByte(Enum.Parse(enumType, name)), enumType.Name + "." + name);
            }
        }

        [Test]
        public void CharacterLoadCode_ValuesPinned()
        {
            AssertEnum(typeof(CharacterLoadCode), ("Success", 0), ("CharacterNotFound", 1), ("AccountMismatch", 2),
                ("CorruptRecord", 3), ("ItemDatabaseMiss", 4), ("DatabaseError", 255));
        }

        [Test]
        public void CharacterSaveResult_ValuesPinned()
        {
            AssertEnum(typeof(CharacterSaveResult), ("Success", 0), ("CharacterNotFound", 1),
                ("ConcurrencyConflict", 2), ("DatabaseError", 255));
        }

        [Test]
        public void PendingPurchaseResult_ValuesPinned()
        {
            AssertEnum(typeof(PendingPurchaseResult), ("Success", 0), ("InsufficientGold", 1),
                ("ConcurrencyConflict", 2), ("DatabaseError", 255));
        }

        [Test]
        public void IrreversibleOutcomeTrigger_ValuesPinned()
        {
            AssertEnum(typeof(IrreversibleOutcomeTrigger), ("LevelUp", 0), ("EnhancementResult", 1),
                ("RespecCommit", 2), ("ItemConsumption", 3));
        }

        [Test]
        public void SessionEndReason_ValuesPinned()
        {
            AssertEnum(typeof(SessionEndReason), ("CleanLogout", 0), ("SessionTTLExpiry", 1), ("ZoneClosed", 2));
        }

        [Test]
        public void PendingPurchaseState_ValuesPinned()
        {
            AssertEnum(typeof(PendingPurchaseState), ("GoldDebited", 0), ("Refunded", 1), ("Completed", 2));
        }

        [Test]
        public void ICharacterPersistence_HasNineMethodsReturningTasks()
        {
            MethodInfo[] methods = typeof(ICharacterPersistence).GetMethods();
            Assert.AreEqual(9, methods.Length);
            foreach (MethodInfo m in methods)
            {
                bool isTask = m.ReturnType == typeof(Task)
                    || (m.ReturnType.IsGenericType && m.ReturnType.GetGenericTypeDefinition() == typeof(Task<>));
                Assert.IsTrue(isTask, m.Name + " return type");
            }
        }

        [Test]
        public void ICharacterPersistence_EveryMethodTakesCancellationTokenLast()
        {
            foreach (MethodInfo m in typeof(ICharacterPersistence).GetMethods())
            {
                ParameterInfo[] ps = m.GetParameters();
                Assert.IsTrue(ps.Length > 0, m.Name);
                Assert.AreEqual(typeof(CancellationToken), ps[ps.Length - 1].ParameterType, m.Name);
            }
        }

        [Test]
        public void ICharacterPersistence_MethodNamesAreTheNineOfCrCp1()
        {
            string[] names = typeof(ICharacterPersistence).GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] expected =
            {
                "BeginPurchase", "CompletePurchase", "CreateStub", "LoadCharacter", "LoadOutstandingPurchases",
                "RefundPurchase", "SaveIrreversibleOutcome", "SaveSession", "SaveSession",
            };
            CollectionAssert.AreEqual(expected, names);
        }

        [Test]
        public void ICharacterPersistence_CreateStubClassTypeIsByte()
        {
            MethodInfo m = typeof(ICharacterPersistence).GetMethod("CreateStub");
            Assert.IsNotNull(m);
            Assert.AreEqual(typeof(byte), m.GetParameters()[2].ParameterType);
        }

        [Test]
        public void ICharacterPersistence_SaveSessionHasTwoOverloadsOneWithFloat()
        {
            MethodInfo[] overloads = typeof(ICharacterPersistence).GetMethods().Where(m => m.Name == "SaveSession").ToArray();
            Assert.AreEqual(2, overloads.Length);
            Assert.AreEqual(1, overloads.Count(m => m.GetParameters().Any(p => p.ParameterType == typeof(float))));
        }

        [Test]
        public void AccountID_DefaultEqualsInvalid()
        {
            Assert.AreEqual(AccountID.Invalid, default(AccountID));
            Assert.AreEqual(0u, AccountID.Invalid.RawValue);
        }

        [Test]
        public void AccountID_EqualityFollowsRawValue()
        {
            Assert.IsTrue(new AccountID(5u) == new AccountID(5u));
            Assert.IsTrue(new AccountID(5u).Equals(new AccountID(5u)));
            Assert.IsTrue(new AccountID(5u) != new AccountID(6u));
        }

        [Test]
        public void ZoneID_DefaultEqualsInvalid()
        {
            Assert.AreEqual(ZoneID.Invalid, default(ZoneID));
            Assert.AreEqual(0u, ZoneID.Invalid.RawValue);
        }

        [Test]
        public void ZoneID_EqualityFollowsRawValue()
        {
            Assert.IsTrue(new ZoneID(5u) == new ZoneID(5u));
            Assert.IsTrue(new ZoneID(5u).Equals(new ZoneID(5u)));
            Assert.IsTrue(new ZoneID(5u) != new ZoneID(6u));
        }

        [Test]
        public void IdTypes_LiveInFoundationAssembly()
        {
            Assembly foundation = typeof(CharacterID).Assembly;
            Assert.AreSame(foundation, typeof(AccountID).Assembly);
            Assert.AreSame(foundation, typeof(ZoneID).Assembly);
        }

        [Test]
        public void ContractTypes_LiveInServerLogicAssembly()
        {
            Assembly serverLogic = typeof(CurrencySystem).Assembly;
            Type[] types =
            {
                typeof(ICharacterPersistence), typeof(CharacterRecord), typeof(CharacterLoadResult),
                typeof(PendingPurchaseRecord), typeof(GearSlotRecord), typeof(CharacterPersistenceConstants),
                typeof(CharacterLoadCode), typeof(CharacterSaveResult), typeof(PendingPurchaseResult),
                typeof(IrreversibleOutcomeTrigger), typeof(SessionEndReason), typeof(PendingPurchaseState),
            };
            foreach (Type t in types)
            {
                Assert.AreSame(serverLogic, t.Assembly, t.Name);
            }
        }

        private static readonly (string Name, Type Type)[] ExpectedRecordProperties =
        {
            ("CharacterID", typeof(CharacterID)), ("AccountID", typeof(AccountID)), ("CharacterName", typeof(string)),
            ("ClassType", typeof(byte)), ("Level", typeof(int)), ("Experience", typeof(int)), ("HeldFreePoints", typeof(int)),
            ("STR", typeof(int)), ("DEX", typeof(int)), ("VIT", typeof(int)), ("INT", typeof(int)),
            ("MaxHP", typeof(int)), ("MaxMP", typeof(int)), ("AttackPower", typeof(int)), ("Defense", typeof(int)),
            ("MagicDefense", typeof(int)), ("CritChance", typeof(float)), ("CritMultiplier", typeof(float)),
            ("AttackRange", typeof(float)), ("AttackSpeedMultiplier", typeof(float)), ("MovementSpeed", typeof(float)),
            ("CurrentHP", typeof(float)), ("CurrentMP", typeof(float)), ("GoldBalance", typeof(uint)),
            ("GoldVersion", typeof(uint)), ("LastZoneID", typeof(ZoneID)), ("SaveVersion", typeof(uint)),
            ("CreatedAtUtc", typeof(DateTime)), ("LastSavedAtUtc", typeof(DateTime)),
            ("GearSlots", typeof(GearSlotRecord[])), ("InventorySlots", typeof(ItemID[])),
            ("InventoryItemCounts", typeof(int[])), ("InventoryEnhancementLevels", typeof(byte[])),
        };

        [Test]
        public void CharacterRecord_HasEveryListedPropertyWithExactType()
        {
            foreach ((string name, Type type) in ExpectedRecordProperties)
            {
                PropertyInfo p = typeof(CharacterRecord).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(p, name + " missing");
                Assert.AreEqual(type, p.PropertyType, name);
            }

            // "These members and no others": a new property must be added to the list above.
            PropertyInfo[] all = typeof(CharacterRecord).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            Assert.AreEqual(ExpectedRecordProperties.Length, all.Length,
                "CharacterRecord public properties: " + string.Join(", ", all.Select(p => p.Name)));
            Assert.IsEmpty(typeof(CharacterRecord).GetFields(BindingFlags.Public | BindingFlags.Instance),
                "CharacterRecord has no public fields");
        }

        [Test]
        public void CharacterRecord_HasNoNotPersistedMembers()
        {
            string[] forbidden = { "Buff", "Modifier", "PrestigeBand", "Appearance", "IsTransitioning" };
            foreach (MemberInfo m in typeof(CharacterRecord).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (string f in forbidden)
                {
                    Assert.IsFalse(m.Name.Contains(f), m.Name + " contains " + f);
                }
            }
        }

        [Test]
        public void CharacterRecord_NewRecordHasSevenGearSlotsAndTwentyInventorySlots()
        {
            var record = new CharacterRecord();
            Assert.AreEqual(CharacterPersistenceConstants.GEAR_SLOT_COUNT, record.GearSlots.Length);
            Assert.AreEqual(InventoryConstants.INVENTORY_SLOT_COUNT, record.InventorySlots.Length);
            Assert.AreEqual(InventoryConstants.INVENTORY_SLOT_COUNT, record.InventoryItemCounts.Length);
            Assert.AreEqual(InventoryConstants.INVENTORY_SLOT_COUNT, record.InventoryEnhancementLevels.Length);
        }

        [Test]
        public void CharacterLoadResult_FieldNamesAndTypes()
        {
            AssertField(typeof(CharacterLoadResult), "Code", typeof(CharacterLoadCode));
            AssertField(typeof(CharacterLoadResult), "LastZoneID", typeof(ZoneID));
            Assert.AreEqual(2, typeof(CharacterLoadResult).GetFields(BindingFlags.Public | BindingFlags.Instance).Length);
        }

        [Test]
        public void PendingPurchaseRecord_FieldNamesAndTypes()
        {
            Type t = typeof(PendingPurchaseRecord);
            AssertField(t, "Id", typeof(long));
            AssertField(t, "CharId", typeof(CharacterID));
            AssertField(t, "RequestId", typeof(uint));
            AssertField(t, "ItemId", typeof(uint));
            AssertField(t, "Quantity", typeof(int));
            AssertField(t, "TotalCost", typeof(uint));
            AssertField(t, "State", typeof(PendingPurchaseState));
            Assert.AreEqual(7, t.GetFields(BindingFlags.Public | BindingFlags.Instance).Length);
        }

        private static void AssertField(Type owner, string name, Type type)
        {
            FieldInfo f = owner.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(f, owner.Name + "." + name + " missing");
            Assert.AreEqual(type, f.FieldType, owner.Name + "." + name);
        }

        [Test]
        public void CharacterRecord_CloneIsDeepAndEqualInScalars()
        {
            var original = new CharacterRecord
            {
                CharacterID = new CharacterID(11u),
                AccountID = new AccountID(22u),
                CharacterName = "Tester",
                ClassType = 3,
                Level = 9,
                Experience = 1234,
                HeldFreePoints = 2,
                STR = 5,
                DEX = 6,
                VIT = 7,
                INT = 8,
                MaxHP = 300,
                MaxMP = 150,
                AttackPower = 41,
                Defense = 17,
                MagicDefense = 13,
                CritChance = 0.25f,
                CritMultiplier = 1.5f,
                AttackRange = 2.5f,
                AttackSpeedMultiplier = 1.25f,
                MovementSpeed = 4.5f,
                CurrentHP = 123.5f,
                CurrentMP = 64.5f,
                GoldBalance = 777u,
                GoldVersion = 4u,
                LastZoneID = new ZoneID(8u),
                SaveVersion = 6u,
                CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                LastSavedAtUtc = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            };
            original.GearSlots[0] = new GearSlotRecord(new ItemID(100u), 4);
            original.InventorySlots[3] = new ItemID(200u);
            original.InventoryItemCounts[3] = 7;
            original.InventoryEnhancementLevels[3] = 2;

            CharacterRecord clone = original.Clone();

            Assert.AreNotSame(original, clone);
            Assert.AreNotSame(original.GearSlots, clone.GearSlots);
            Assert.AreNotSame(original.InventorySlots, clone.InventorySlots);
            Assert.AreNotSame(original.InventoryItemCounts, clone.InventoryItemCounts);
            Assert.AreNotSame(original.InventoryEnhancementLevels, clone.InventoryEnhancementLevels);

            var blank = new CharacterRecord();
            foreach ((string name, Type _) in ExpectedRecordProperties.Where(e => !e.Type.IsArray))
            {
                PropertyInfo p = typeof(CharacterRecord).GetProperty(name);
                // A scalar left at its default would let a Clone that forgets it pass.
                Assert.AreNotEqual(p.GetValue(blank), p.GetValue(original), name + " is not set by this test");
                Assert.AreEqual(p.GetValue(original), p.GetValue(clone), name);
            }
            Assert.AreEqual(new ItemID(100u), clone.GearSlots[0].ItemId);
            Assert.AreEqual(4, clone.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(200u), clone.InventorySlots[3]);
            Assert.AreEqual(7, clone.InventoryItemCounts[3]);
            Assert.AreEqual(2, clone.InventoryEnhancementLevels[3]);

            clone.GearSlots[0] = GearSlotRecord.Empty;
            clone.InventorySlots[3] = ItemID.Invalid;
            clone.InventoryItemCounts[3] = 0;
            clone.InventoryEnhancementLevels[3] = 0;

            Assert.AreEqual(new ItemID(100u), original.GearSlots[0].ItemId);
            Assert.AreEqual(4, original.GearSlots[0].EnhancementLevel);
            Assert.AreEqual(new ItemID(200u), original.InventorySlots[3]);
            Assert.AreEqual(7, original.InventoryItemCounts[3]);
            Assert.AreEqual(2, original.InventoryEnhancementLevels[3]);
        }

        // Every method of CR-CP-1 with its exact return type and parameter list (classType is a byte: story decision 1).
        private static readonly (string Name, Type Return, Type[] Parameters)[] ExpectedMethods =
        {
            ("LoadCharacter", typeof(Task<CharacterLoadResult>),
                new[] { typeof(CharacterID), typeof(AccountID), typeof(CancellationToken) }),
            ("SaveIrreversibleOutcome", typeof(Task<CharacterSaveResult>),
                new[] { typeof(CharacterID), typeof(IrreversibleOutcomeTrigger), typeof(CancellationToken) }),
            ("SaveSession", typeof(Task<CharacterSaveResult>),
                new[] { typeof(CharacterID), typeof(SessionEndReason), typeof(CancellationToken) }),
            ("SaveSession", typeof(Task<CharacterSaveResult>),
                new[] { typeof(CharacterID), typeof(SessionEndReason), typeof(float), typeof(CancellationToken) }),
            ("CreateStub", typeof(Task),
                new[] { typeof(AccountID), typeof(CharacterID), typeof(byte), typeof(CancellationToken) }),
            ("BeginPurchase", typeof(Task<PendingPurchaseResult>),
                new[] { typeof(CharacterID), typeof(PendingPurchaseRecord), typeof(CancellationToken) }),
            ("CompletePurchase", typeof(Task<CharacterSaveResult>),
                new[] { typeof(long), typeof(CancellationToken) }),
            ("RefundPurchase", typeof(Task<CharacterSaveResult>),
                new[] { typeof(long), typeof(CharacterID), typeof(uint), typeof(CancellationToken) }),
            ("LoadOutstandingPurchases", typeof(Task<IReadOnlyList<PendingPurchaseRecord>>),
                new[] { typeof(CharacterID), typeof(CancellationToken) }),
        };

        [Test]
        public void ICharacterPersistence_EverySignatureMatchesCrCp1()
        {
            Assert.AreEqual(ExpectedMethods.Length, typeof(ICharacterPersistence).GetMethods().Length);
            foreach ((string name, Type returnType, Type[] parameters) in ExpectedMethods)
            {
                string signature = name + "(" + string.Join(", ", parameters.Select(p => p.Name)) + ")";
                MethodInfo m = typeof(ICharacterPersistence).GetMethods().SingleOrDefault(candidate =>
                    candidate.Name == name
                    && candidate.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));
                Assert.IsNotNull(m, signature + " missing");
                Assert.AreEqual(returnType, m.ReturnType, signature + " return type");
            }
        }

        private static bool IsReadOnlyStruct(Type type)
        {
            return type.IsValueType && type.GetCustomAttributes(false)
                .Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }

        private static void AssertIdShape(Type idType, Type equatable)
        {
            Assert.IsTrue(IsReadOnlyStruct(idType), idType.Name + " is a readonly struct");
            Assert.IsTrue(equatable.IsAssignableFrom(idType), idType.Name + " implements IEquatable of itself");
            FieldInfo[] fields = idType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.AreEqual(1, fields.Length, idType.Name + " instance field count");
            Assert.AreEqual(typeof(uint), fields[0].FieldType, idType.Name + " wraps a uint");
            Assert.AreEqual(typeof(uint), idType.GetProperty("RawValue").PropertyType, idType.Name + ".RawValue");
        }

        [Test]
        public void AccountID_IsReadonlyStructOverUint()
        {
            AssertIdShape(typeof(AccountID), typeof(IEquatable<AccountID>));
            Assert.AreEqual(5u, new AccountID(5u).RawValue);
        }

        [Test]
        public void ZoneID_IsReadonlyStructOverUint()
        {
            AssertIdShape(typeof(ZoneID), typeof(IEquatable<ZoneID>));
            Assert.AreEqual(5u, new ZoneID(5u).RawValue);
        }

        [Test]
        public void AccountID_ObjectEqualsHashAndToStringFollowRawValue()
        {
            var id = new AccountID(5u);

            Assert.IsTrue(id.Equals((object)new AccountID(5u)));
            Assert.IsFalse(id.Equals((object)new AccountID(6u)));
            Assert.IsFalse(id.Equals((object)"5"));
            Assert.AreEqual(id.GetHashCode(), new AccountID(5u).GetHashCode());
            StringAssert.Contains("5", id.ToString());
        }

        [Test]
        public void ZoneID_ObjectEqualsHashAndToStringFollowRawValue()
        {
            var id = new ZoneID(5u);

            Assert.IsTrue(id.Equals((object)new ZoneID(5u)));
            Assert.IsFalse(id.Equals((object)new ZoneID(6u)));
            Assert.IsFalse(id.Equals((object)"5"));
            Assert.AreEqual(id.GetHashCode(), new ZoneID(5u).GetHashCode());
            StringAssert.Contains("5", id.ToString());
        }

        [Test]
        public void DataStructs_AreReadonlyWithReadonlyFields()
        {
            foreach (Type t in new[] { typeof(CharacterLoadResult), typeof(PendingPurchaseRecord), typeof(GearSlotRecord) })
            {
                Assert.IsTrue(IsReadOnlyStruct(t), t.Name + " is a readonly struct");
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.IsTrue(f.IsInitOnly, t.Name + "." + f.Name + " is readonly");
                }
            }
        }

        [Test]
        public void CharacterLoadResult_ConstructorAssignsEachField()
        {
            var result = new CharacterLoadResult(CharacterLoadCode.CorruptRecord, new ZoneID(8u));

            Assert.AreEqual(CharacterLoadCode.CorruptRecord, result.Code);
            Assert.AreEqual(new ZoneID(8u), result.LastZoneID);
        }

        [Test]
        public void PendingPurchaseRecord_ConstructorAssignsEachField()
        {
            // Every value is different, so two fields of the same type swapped in the constructor fail.
            var record = new PendingPurchaseRecord(91L, new CharacterID(92u), 93u, 94u, 95, 96u, PendingPurchaseState.Completed);

            Assert.AreEqual(91L, record.Id);
            Assert.AreEqual(new CharacterID(92u), record.CharId);
            Assert.AreEqual(93u, record.RequestId);
            Assert.AreEqual(94u, record.ItemId);
            Assert.AreEqual(95, record.Quantity);
            Assert.AreEqual(96u, record.TotalCost);
            Assert.AreEqual(PendingPurchaseState.Completed, record.State);
        }

        [Test]
        public void GearSlotRecord_ConstructorAssignsEachFieldAndEmptyIsDefault()
        {
            var slot = new GearSlotRecord(new ItemID(100u), 4);

            Assert.AreEqual(new ItemID(100u), slot.ItemId);
            Assert.AreEqual((byte)4, slot.EnhancementLevel);
            Assert.AreEqual(ItemID.Invalid, GearSlotRecord.Empty.ItemId);
            Assert.AreEqual((byte)0, GearSlotRecord.Empty.EnhancementLevel);
            Assert.AreEqual(ItemID.Invalid, default(GearSlotRecord).ItemId, "a default slot is the empty slot");
        }

        [Test]
        public void CharacterRecord_NewRecordHasEverySlotEmpty()
        {
            var record = new CharacterRecord();

            foreach (GearSlotRecord slot in record.GearSlots)
            {
                Assert.AreEqual(ItemID.Invalid, slot.ItemId);
                Assert.AreEqual((byte)0, slot.EnhancementLevel);
            }
            for (int i = 0; i < record.InventorySlots.Length; i++)
            {
                Assert.AreEqual(ItemID.Invalid, record.InventorySlots[i], "slot " + i);
                Assert.AreEqual(0, record.InventoryItemCounts[i], "count " + i);
                Assert.AreEqual((byte)0, record.InventoryEnhancementLevels[i], "level " + i);
            }
        }

        [Test]
        public void CharacterRecord_ArrayPropertiesHaveNoSetter()
        {
            foreach ((string name, Type _) in ExpectedRecordProperties.Where(e => e.Type.IsArray))
            {
                PropertyInfo p = typeof(CharacterRecord).GetProperty(name);
                Assert.IsNull(p.GetSetMethod(true), name + " must not be replaceable (fixed-capacity array)");
            }
        }

        [Test]
        public void Constants_HaveTheSpecifiedValues()
        {
            Assert.AreEqual(10, CharacterPersistenceConstants.CHARACTER_LOAD_TIMEOUT_SECONDS);
            Assert.AreEqual(5, CharacterPersistenceConstants.PERSISTENCE_WRITE_TIMEOUT_SECONDS);
            Assert.AreEqual(50, CharacterPersistenceConstants.PERSISTENCE_WRITE_WARNING_MS);
            Assert.AreEqual(100, CharacterPersistenceConstants.PERSISTENCE_WRITE_CRITICAL_MS);
            Assert.AreEqual(7, CharacterPersistenceConstants.GEAR_SLOT_COUNT);
        }

        [Test]
        public void Constants_GearSlotCountEqualsGearSlotMembers()
        {
            Assert.AreEqual(Enum.GetValues(typeof(GearSlot)).Length, CharacterPersistenceConstants.GEAR_SLOT_COUNT);
        }
    }
}
