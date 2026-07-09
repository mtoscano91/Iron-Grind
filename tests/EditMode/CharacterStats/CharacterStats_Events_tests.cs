using System;
using NUnit.Framework;
using IronGrind.CharacterStats;

namespace IronGrind.Tests.EditMode.CharacterStats
{
    /// <summary>
    /// EditMode unit tests for CharacterStats event infrastructure — Story 005 acceptance criteria.
    /// Covers OnStatChanged synchronous firing, re-entrance guard, unsubscribe correctness,
    /// and OnEntityDied re-entrance guard.
    /// </summary>
    [TestFixture]
    public class CharacterStats_Events_Tests
    {
        private IronGrind.CharacterStats.CharacterStats _stats;
        private EntityID _player;

        private static readonly BuffID WarriorCry  = (BuffID)601u;
        private static readonly BuffID ReentryTest = (BuffID)602u;
        private static readonly BuffID TestA       = (BuffID)603u;
        private static readonly BuffID TestB       = (BuffID)604u;

        [SetUp]
        public void SetUp()
        {
            _stats  = CharacterStatsFixture.Create();
            _player = CharacterStatsFixture.PlayerEntityId;
        }

        // -------------------------------------------------------------------
        // AC-29: OnStatChanged fires synchronously on AddBuffModifier and
        // RemoveBuffModifier; notifiedValue reflects the post-change state.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_OnStatChanged_AddAndRemoveBuff_FiresSynchronously()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            bool handlerFired = false;
            float notifiedValue = 0f;

            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (id, stat) =>
            {
                if (stat != StatID.AttackPower) return;
                handlerFired = true;
                notifiedValue = _stats.GetEffectiveStat(id, stat);
            };
            _stats.Subscribe(handler);

            // Act 1 — AddBuffModifier; event must fire before method returns
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 10, WarriorCry));

            // Assert 1
            Assert.IsTrue(handlerFired,
                "OnStatChanged must fire synchronously — handlerFired must be true immediately after AddBuffModifier returns.");
            Assert.AreEqual(130f, notifiedValue, 0.0f,
                "notifiedValue must be 130 (100 base + 30 flat); modifier already applied when event fires.");

            // Act 2 — RemoveBuffModifier; event must fire again
            handlerFired = false;
            notifiedValue = 0f;
            _stats.RemoveBuffModifier(_player, StatID.AttackPower, WarriorCry);

            // Assert 2
            Assert.IsTrue(handlerFired,
                "OnStatChanged must fire synchronously on RemoveBuffModifier.");
            Assert.AreEqual(100f, notifiedValue, 0.0f,
                "notifiedValue must be 100 (base only) after buff removal.");
        }

        // AC-29 edge: two different stats modified — handler fires once per stat change.
        [Test]
        public void CharacterStats_OnStatChanged_TwoDifferentStats_FiresPerStatChange()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            _stats.SetBaseStat(_player, StatID.Defense, 50);
            int fireCount = 0;
            _stats.Subscribe((id, stat) => fireCount++);

            // Act — modify two independent stats
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestA));
            _stats.AddBuffModifier(_player, StatID.Defense,
                new BuffModifierEntry(5f, 0f, 5, TestB));

            // Assert — one event per stat change, not one total
            Assert.AreEqual(2, fireCount,
                "OnStatChanged must fire once per stat change — two distinct stats modified means exactly two events.");
        }

        // -------------------------------------------------------------------
        // AC-29b: Re-entrance guard throws in UNITY_EDITOR / DEVELOPMENT_BUILD.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_OnStatChanged_WriteInsideHandler_ThrowsInvalidOperationException()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            bool exceptionThrown = false;

            _stats.Subscribe((id, stat) =>
            {
                if (stat != StatID.AttackPower) return;
                try
                {
                    // Write inside the handler — re-entrance guard must throw
                    _stats.AddBuffModifier(id, stat,
                        new BuffModifierEntry(10f, 0f, 5, ReentryTest));
                }
                catch (InvalidOperationException)
                {
                    exceptionThrown = true;
                }
            });

            // Act — outer AddBuffModifier fires handler; inner one must be rejected
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(30f, 0f, 10, WarriorCry));

            // Assert
            Assert.IsTrue(exceptionThrown,
                "Re-entrant AddBuffModifier inside OnStatChanged handler must throw InvalidOperationException in UNITY_EDITOR builds.");
            Assert.AreEqual(130, _stats.GetEffectiveStat(_player, StatID.AttackPower),
                "Outer add (WarriorCry +30) must be applied; inner re-entrant add (ReentryTest) must be rejected — no corruption.");
        }

        // AC-29b edge: GetEffectiveStat inside handler does NOT throw (read-only permitted).
        [Test]
        public void CharacterStats_OnStatChanged_ReadInsideHandler_DoesNotThrow()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            bool readSucceeded = false;

            _stats.Subscribe((id, stat) =>
            {
                if (stat != StatID.AttackPower) return;
                // Read-only query inside handler — must not throw
                int _ = _stats.GetEffectiveStat(id, stat);
                readSucceeded = true;
            });

            // Act
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestA));

            // Assert
            Assert.IsTrue(readSucceeded,
                "GetEffectiveStat called inside OnStatChanged handler must not throw — read-only queries are always permitted.");
        }

        // -------------------------------------------------------------------
        // [NEW] Unsubscribe correctness: handler not called after Unsubscribe.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_OnStatChanged_Unsubscribe_HandlerSilentAfterUnsubscribe()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            int callCount = 0;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (id, stat) => callCount++;
            _stats.Subscribe(handler);

            // Confirm active subscription
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestA));
            Assert.AreEqual(1, callCount,
                "Precondition: handler called once (subscription confirmed active).");

            // Act — unsubscribe then fire
            _stats.Unsubscribe(handler);
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestB));

            // Assert — callCount must not change
            Assert.AreEqual(1, callCount,
                "Handler must NOT be called after Unsubscribe — callCount must remain 1.");
        }

        // [NEW] Unsubscribe edge: handler that was never subscribed — no-op, no exception.
        [Test]
        public void CharacterStats_OnStatChanged_Unsubscribe_HandlerNeverSubscribed_IsNoOp()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            int callCount = 0;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler activeHandler  = (id, stat) => callCount++;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler neverSubscribed = (id, stat) => { };
            _stats.Subscribe(activeHandler);

            // Act — unsubscribe a handler that was never registered; must not throw
            Assert.DoesNotThrow(() => _stats.Unsubscribe(neverSubscribed),
                "Unsubscribe of a handler that was never subscribed must not throw.");

            // Active subscription must be unaffected
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestA));

            Assert.AreEqual(1, callCount,
                "Active subscription must still fire after unsubscribing an unrelated, never-subscribed handler.");
        }

        // [NEW] Unsubscribe edge: subscribe → unsubscribe → re-subscribe → fires again.
        [Test]
        public void CharacterStats_OnStatChanged_Unsubscribe_ResubscribeFiresAgain()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.AttackPower, 100);
            int callCount = 0;
            IronGrind.CharacterStats.CharacterStats.StatChangedHandler handler = (id, stat) => callCount++;

            _stats.Subscribe(handler);
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestA));
            Assert.AreEqual(1, callCount, "Precondition: fired once after initial subscribe.");

            _stats.Unsubscribe(handler);
            _stats.RemoveBuffModifier(_player, StatID.AttackPower, TestA);
            Assert.AreEqual(1, callCount, "Must not fire after unsubscribe.");

            // Re-subscribe and fire
            _stats.Subscribe(handler);
            _stats.AddBuffModifier(_player, StatID.AttackPower,
                new BuffModifierEntry(10f, 0f, 5, TestB));

            Assert.AreEqual(2, callCount,
                "Handler must fire again after re-subscribe.");
        }

        // -------------------------------------------------------------------
        // [NEW] OnEntityDied re-entrance guard.
        // -------------------------------------------------------------------

        [Test]
        public void CharacterStats_OnEntityDied_WriteInsideHandler_ThrowsInvalidOperationException()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 50f); // CurrentHP = 50f
            bool exceptionThrown = false;

            _stats.Subscribe(new IronGrind.CharacterStats.CharacterStats.EntityDiedHandler(id =>
            {
                try
                {
                    // Write inside OnEntityDied handler — must throw
                    _stats.ApplyDamage(id, 1f);
                }
                catch (InvalidOperationException)
                {
                    exceptionThrown = true;
                }
            }));

            // Act — kill entity; fires OnEntityDied
            _stats.ApplyDamage(_player, 50f);

            // Assert
            Assert.IsTrue(exceptionThrown,
                "ApplyDamage inside OnEntityDied handler must throw InvalidOperationException (re-entrance guard).");
            Assert.AreEqual(0f, _stats.GetCurrentHP(_player), 0.0f,
                "CurrentHP must be 0f — death was processed before handler was invoked.");
        }

        // [NEW] OnEntityDied edge: GetEffectiveStat inside handler does NOT throw.
        [Test]
        public void CharacterStats_OnEntityDied_ReadInsideHandler_DoesNotThrow()
        {
            // Arrange
            _stats.SetBaseStat(_player, StatID.MaxHP, 1000);
            _stats.ApplyRegen(_player, 50f);
            bool readSucceeded = false;

            _stats.Subscribe(new IronGrind.CharacterStats.CharacterStats.EntityDiedHandler(id =>
            {
                // Read-only query inside handler — must not throw
                int _ = _stats.GetEffectiveStat(id, StatID.MaxHP);
                readSucceeded = true;
            }));

            // Act
            _stats.ApplyDamage(_player, 50f);

            // Assert
            Assert.IsTrue(readSucceeded,
                "GetEffectiveStat inside OnEntityDied handler must not throw — read-only queries are always permitted.");
        }
    }
}
