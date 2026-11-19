using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.ServerLogic.PartySystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace IronGrind.Tests.EditMode.PartySystem
{
    /// <summary>
    /// EditMode tests for Party System Story 002: <see cref="PartyRegistry"/> (AC-1 solo ids, AC-2 create and
    /// join, AC-3 limits, AC-4 compaction, AC-5 queries, AC-6 lifecycle and counter exhaustion). Every failure-path
    /// test asserts through a registry snapshot that the failed call changed no state.
    /// </summary>
    [TestFixture]
    internal sealed class PartySystem_Registry_Tests
    {
        private const int REGISTER_COUNT = 50;
        private const int QUERY_ITERATIONS = 1000;
        private const string EXHAUSTED_PATTERN = "party id counter exhausted";
        private const uint UNKNOWN_PARTY_RAW = 9999u;
        private const uint UNKNOWN_CHARACTER_RAW = 77u;
        private const uint SPECTATOR_RAW = 9u;

        /// <summary>A frozen view of the registry, compared against the live registry after a failed call.</summary>
        private sealed class RegistrySnapshot
        {
            private int _partyCount;
            private uint[] _characters;
            private PartyID[] _parties;
            private PartyID[] _partyOfCharacter;
            private bool[] _registered;
            private bool[] _exists;
            private PartyState[] _states;
            private int[] _memberCounts;
            private CharacterID[][] _members;
            private EntityID[][] _entities;
            private uint[] _sequenceIds;
            private PartyLifecycleState[] _lifecycle;

            public static RegistrySnapshot Capture(PartyRegistry reg, uint[] characters, PartyID[] parties)
            {
                var snap = new RegistrySnapshot
                {
                    _partyCount = reg.PartyCount,
                    _characters = characters,
                    _parties = parties,
                    _partyOfCharacter = new PartyID[characters.Length],
                    _registered = new bool[characters.Length],
                    _exists = new bool[parties.Length],
                    _states = new PartyState[parties.Length],
                    _memberCounts = new int[parties.Length],
                    _members = new CharacterID[parties.Length][],
                    _entities = new EntityID[parties.Length][],
                    _sequenceIds = new uint[parties.Length],
                    _lifecycle = new PartyLifecycleState[parties.Length],
                };

                for (int i = 0; i < characters.Length; i++)
                {
                    snap._partyOfCharacter[i] = reg.GetPartyID(Ch(characters[i]));
                    snap._registered[i] = reg.IsRegistered(Ch(characters[i]));
                }

                for (int p = 0; p < parties.Length; p++)
                {
                    bool exists = reg.TryGetParty(parties[p], out PartyState state);
                    snap._exists[p] = exists;
                    snap._states[p] = state;
                    if (!exists)
                    {
                        continue;
                    }

                    snap._memberCounts[p] = state.MemberCount;
                    snap._sequenceIds[p] = state.SequenceId;
                    snap._lifecycle[p] = state.State;
                    snap._members[p] = new CharacterID[state.MemberCount];
                    snap._entities[p] = new EntityID[state.MemberCount];
                    for (int m = 0; m < state.MemberCount; m++)
                    {
                        snap._members[p][m] = state.GetMemberAtIndex(m);
                        snap._entities[p][m] = state.GetEntityAtIndex(m);
                    }
                }

                return snap;
            }

            public void AssertUnchanged(PartyRegistry reg)
            {
                Assert.AreEqual(_partyCount, reg.PartyCount, "PartyCount changed");
                for (int i = 0; i < _characters.Length; i++)
                {
                    Assert.AreEqual(_partyOfCharacter[i], reg.GetPartyID(Ch(_characters[i])),
                        "party of character " + _characters[i]);
                    Assert.AreEqual(_registered[i], reg.IsRegistered(Ch(_characters[i])),
                        "registered flag of character " + _characters[i]);
                }

                for (int p = 0; p < _parties.Length; p++)
                {
                    bool exists = reg.TryGetParty(_parties[p], out PartyState state);
                    Assert.AreEqual(_exists[p], exists, "existence of party " + _parties[p]);
                    if (!exists)
                    {
                        continue;
                    }

                    Assert.AreSame(_states[p], state, "party state object replaced");
                    Assert.AreEqual(_memberCounts[p], state.MemberCount, "MemberCount of party " + _parties[p]);
                    Assert.AreEqual(_sequenceIds[p], state.SequenceId, "SequenceId of party " + _parties[p]);
                    Assert.AreEqual(_lifecycle[p], state.State, "State of party " + _parties[p]);
                    Assert.AreEqual(_memberCounts[p], state.Members.Count, "Members.Count of party " + _parties[p]);
                    for (int m = 0; m < _memberCounts[p]; m++)
                    {
                        Assert.AreEqual(_members[p][m], state.GetMemberAtIndex(m), "member " + m);
                        Assert.AreEqual(_members[p][m], state.Members[m], "snapshot member " + m);
                        Assert.AreEqual(_entities[p][m], state.GetEntityAtIndex(m), "entity " + m);
                    }
                }
            }
        }

        private static CharacterID Ch(uint n) => new CharacterID(n);

        private static EntityID En(uint n) => new EntityID(1000u + n);

        private static PartyRegistry NewRegistry(int maxPartySize = PartySystemConfigValues.MAX_PARTY_SIZE_DEFAULT)
        {
            return new PartyRegistry(new PartySystemConfigValues(
                maxPartySize,
                PartySystemConfigValues.XP_PARTY_DEDUCTION_RATE_DEFAULT,
                PartySystemConfigValues.LEVEL_RANGE_GATE_DEFAULT,
                PartySystemConfigValues.INVITE_TIMEOUT_SECONDS_DEFAULT,
                PartySystemConfigValues.INVITE_RETRY_SECONDS_DEFAULT,
                PartySystemConfigValues.RECONNECT_GRACE_SECONDS_DEFAULT,
                PartySystemConfigValues.HP_DELTA_THRESHOLD_FRACTION_DEFAULT,
                PartySystemConfigValues.DISBAND_TIMEOUT_SECONDS_DEFAULT));
        }

        private static PartyID Register(PartyRegistry reg, uint n)
        {
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RegisterPlayer(Ch(n), En(n), out PartyID solo));
            return solo;
        }

        /// <summary>Registers characters 1..count, makes 1 the leader and adds the rest. Returns the party id.</summary>
        private static PartyID BuildParty(PartyRegistry reg, int count)
        {
            for (uint n = 1; n <= (uint)count; n++)
            {
                Register(reg, n);
            }

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            for (uint n = 2; n <= (uint)count; n++)
            {
                Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(n)));
            }

            return party;
        }

        private static RegistrySnapshot Snap(PartyRegistry reg, uint[] characters, params PartyID[] parties)
        {
            return RegistrySnapshot.Capture(reg, characters, parties);
        }

        private static void AssertMembers(PartyRegistry reg, PartyID party, params uint[] expected)
        {
            IReadOnlyList<CharacterID> members = reg.GetPartyMembers(party);
            Assert.AreEqual(expected.Length, members.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(Ch(expected[i]), members[i], "slot " + i);
                Assert.AreEqual(Ch(expected[i]), reg.GetMemberAtIndex(party, i));
            }
        }

        private static void AssertFreed(PartyState state)
        {
            Assert.IsTrue(state.IsFreed);
            Assert.AreEqual(0, state.MemberCount);
            Assert.AreEqual(0, state.Members.Count);
        }

        // ---- AC-1 ----

        [Test]
        public void test_registry_register_50_players_distinct_non_zero_solo_ids()
        {
            PartyRegistry reg = NewRegistry();
            var seen = new HashSet<uint>();

            for (uint n = 1; n <= REGISTER_COUNT; n++)
            {
                PartyID solo = Register(reg, n);
                Assert.AreNotEqual(PartyID.Uninitialized, solo);
                Assert.IsTrue(seen.Add(solo.RawValue), "duplicate id " + solo);
                Assert.IsTrue(reg.TryGetParty(solo, out PartyState state));
                Assert.IsTrue(state.IsSoloParty);
                Assert.AreEqual(1, state.MemberCount);
                Assert.AreEqual(solo, reg.GetPartyID(Ch(n)));
            }

            Assert.AreEqual(REGISTER_COUNT, reg.PartyCount);
        }

        [Test]
        public void test_registry_first_ids_are_sequential_from_one()
        {
            PartyRegistry reg = NewRegistry();

            Assert.AreEqual(1u, Register(reg, 1).RawValue);
            Assert.AreEqual(2u, Register(reg, 2).RawValue);
        }

        // ---- AC-2 ----

        [Test]
        public void test_registry_create_and_join_forming_then_active()
        {
            PartyRegistry reg = NewRegistry();
            PartyID leaderSolo = Register(reg, 1);
            PartyID joinerSolo = Register(reg, 2);
            Assert.IsTrue(reg.TryGetParty(leaderSolo, out PartyState leaderOldSolo));
            Assert.IsTrue(reg.TryGetParty(joinerSolo, out PartyState joinerOldSolo));

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));

            Assert.IsFalse(reg.TryGetParty(leaderSolo, out _), "leader's old solo id must not resolve");
            Assert.IsTrue(reg.TryGetParty(joinerSolo, out _), "joiner's solo still resolves");
            Assert.AreEqual(2, reg.PartyCount);
            AssertFreed(leaderOldSolo);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            Assert.IsFalse(state.IsSoloParty);
            Assert.AreEqual(PartyLifecycleState.Forming, state.State);
            Assert.AreEqual(1, state.MemberCount);
            Assert.AreEqual(0, state.LeaderIndex);
            Assert.AreEqual(Ch(1), state.GetMemberAtIndex(0));
            Assert.AreEqual(En(1), state.GetEntityAtIndex(0));
            Assert.AreEqual(1u, state.SequenceId);
            Assert.AreEqual(party, reg.GetPartyID(Ch(1)));

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(2)));

            Assert.AreEqual(PartyLifecycleState.Active, state.State);
            Assert.AreEqual(2, state.MemberCount);
            Assert.AreEqual(2u, state.SequenceId);
            Assert.AreEqual(En(2), state.GetEntityAtIndex(1));
            Assert.AreEqual(party, reg.GetPartyID(Ch(2)));
            Assert.IsFalse(reg.TryGetParty(joinerSolo, out _), "joiner's old solo id must not resolve");
            AssertFreed(joinerOldSolo);
            Assert.AreEqual(1, reg.PartyCount);
        }

        [Test]
        public void test_registry_remove_member_increments_sequence_id()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            uint before = state.SequenceId;

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out _));

            Assert.AreEqual(before + 1u, state.SequenceId);
        }

        [Test]
        public void test_registry_create_by_unregistered_leader_not_registered_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            RegistrySnapshot snap = Snap(reg, new[] { 1u });

            PartyRegistryResult result = reg.TryCreateParty(Ch(1), out PartyID party);

            Assert.AreEqual(PartyRegistryResult.NotRegistered, result);
            Assert.AreEqual(PartyID.Uninitialized, party);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_create_by_leader_in_real_party_not_solo_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID existing = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, existing);

            PartyRegistryResult result = reg.TryCreateParty(Ch(2), out PartyID party);

            Assert.AreEqual(PartyRegistryResult.NotSolo, result);
            Assert.AreEqual(PartyID.Uninitialized, party);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_create_by_invalid_character_invalid_character_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);

            PartyRegistryResult result = reg.TryCreateParty(CharacterID.Invalid, out PartyID created);

            Assert.AreEqual(PartyRegistryResult.InvalidCharacter, result);
            Assert.AreEqual(PartyID.Uninitialized, created);
            snap.AssertUnchanged(reg);
        }

        // ---- AC-3 ----

        [Test]
        public void test_registry_fifth_character_joins_full_party_party_full_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 4);
            PartyID fifthSolo = Register(reg, 5);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, 3u, 4u, 5u }, party, fifthSolo);

            PartyRegistryResult result = reg.TryAddMember(party, Ch(5));

            Assert.AreEqual(PartyRegistryResult.PartyFull, result);
            snap.AssertUnchanged(reg);
            Assert.AreEqual(4, reg.GetPartyMembers(party).Count);
            Assert.AreEqual(fifthSolo, reg.GetPartyID(Ch(5)));
        }

        [Test]
        public void test_registry_config_max_party_size_three_refuses_fourth_member_state_unchanged()
        {
            PartyRegistry reg = NewRegistry(3);
            PartyID party = BuildParty(reg, 3);
            PartyID fourthSolo = Register(reg, 4);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, 3u, 4u }, party, fourthSolo);

            PartyRegistryResult result = reg.TryAddMember(party, Ch(4));

            Assert.AreEqual(PartyRegistryResult.PartyFull, result);
            snap.AssertUnchanged(reg);
            Assert.AreEqual(3, reg.GetPartyMembers(party).Count);
        }

        [Test]
        public void test_registry_member_of_party_x_joins_party_y_already_in_party_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID partyX = BuildParty(reg, 2);
            Register(reg, 3);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(3), out PartyID partyY));
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, 3u }, partyX, partyY);

            PartyRegistryResult result = reg.TryAddMember(partyY, Ch(2));

            Assert.AreEqual(PartyRegistryResult.AlreadyInParty, result);
            snap.AssertUnchanged(reg);
            Assert.AreEqual(partyX, reg.GetPartyID(Ch(2)));
            AssertMembers(reg, partyX, 1, 2);
            AssertMembers(reg, partyY, 3);
        }

        [Test]
        public void test_registry_member_joins_own_party_already_in_party_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);

            PartyRegistryResult result = reg.TryAddMember(party, Ch(2));

            Assert.AreEqual(PartyRegistryResult.AlreadyInParty, result);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_add_to_solo_party_solo_party_cannot_accept_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            PartyID otherSolo = Register(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, solo, otherSolo);

            PartyRegistryResult result = reg.TryAddMember(solo, Ch(2));

            Assert.AreEqual(PartyRegistryResult.SoloPartyCannotAccept, result);
            snap.AssertUnchanged(reg);
            AssertMembers(reg, solo, 1);
        }

        [Test]
        public void test_registry_add_to_unknown_party_unknown_party_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u }, solo);

            PartyRegistryResult result = reg.TryAddMember(new PartyID(UNKNOWN_PARTY_RAW), Ch(1));

            Assert.AreEqual(PartyRegistryResult.UnknownParty, result);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_add_unregistered_character_not_registered_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, UNKNOWN_CHARACTER_RAW }, party);

            PartyRegistryResult result = reg.TryAddMember(party, Ch(UNKNOWN_CHARACTER_RAW));

            Assert.AreEqual(PartyRegistryResult.NotRegistered, result);
            snap.AssertUnchanged(reg);
            AssertMembers(reg, party, 1, 2);
            Assert.IsFalse(reg.IsRegistered(Ch(UNKNOWN_CHARACTER_RAW)));
        }

        [Test]
        public void test_registry_add_invalid_character_invalid_character_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);

            PartyRegistryResult result = reg.TryAddMember(party, CharacterID.Invalid);

            Assert.AreEqual(PartyRegistryResult.InvalidCharacter, result);
            snap.AssertUnchanged(reg);
            AssertMembers(reg, party, 1, 2);
        }

        [Test]
        public void test_registry_remove_from_unknown_or_solo_or_non_member_typed_failures_no_change_no_id_burned()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, SPECTATOR_RAW);
            PartyID party = BuildParty(reg, 2);
            uint lastIssued = party.RawValue;
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, SPECTATOR_RAW }, party, solo);

            Assert.AreEqual(PartyRegistryResult.UnknownParty,
                reg.RemoveMember(new PartyID(UNKNOWN_PARTY_RAW), Ch(1), out PartyID a));
            Assert.AreEqual(PartyRegistryResult.SoloParty, reg.RemoveMember(solo, Ch(SPECTATOR_RAW), out PartyID b));
            Assert.AreEqual(PartyRegistryResult.NotMember, reg.RemoveMember(party, Ch(SPECTATOR_RAW), out PartyID c));

            Assert.AreEqual(PartyID.Uninitialized, a);
            Assert.AreEqual(PartyID.Uninitialized, b);
            Assert.AreEqual(PartyID.Uninitialized, c);
            snap.AssertUnchanged(reg);
            AssertMembers(reg, party, 1, 2);
            PartyID next = Register(reg, 3);
            Assert.AreEqual(lastIssued + 1u, next.RawValue, "failed calls must not burn ids");
        }

        [Test]
        public void test_registry_remove_invalid_character_not_member_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);

            PartyRegistryResult result = reg.RemoveMember(party, CharacterID.Invalid, out PartyID solo);

            Assert.AreEqual(PartyRegistryResult.NotMember, result);
            Assert.AreEqual(PartyID.Uninitialized, solo);
            snap.AssertUnchanged(reg);
        }

        // ---- AC-4 ----

        [Test]
        public void test_registry_remove_middle_member_compacts_and_re_solos()
        {
            PartyRegistry reg = NewRegistry();
            PartyID bSolo = Register(reg, 2);
            Register(reg, 1);
            Register(reg, 3);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(2)));
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(3)));
            AssertMembers(reg, party, 1, 2, 3);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            uint sequenceBefore = state.SequenceId;

            PartyRegistryResult result = reg.RemoveMember(party, Ch(2), out PartyID newSolo);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            AssertMembers(reg, party, 1, 3);
            Assert.AreEqual(Ch(1), reg.GetMemberAtIndex(party, 0));
            Assert.AreEqual(Ch(3), reg.GetMemberAtIndex(party, 1));
            Assert.AreEqual(CharacterID.Invalid, reg.GetMemberAtIndex(party, 2));
            Assert.AreEqual(En(1), state.GetEntityAtIndex(0));
            Assert.AreEqual(En(3), state.GetEntityAtIndex(1));
            Assert.AreEqual(EntityID.Invalid, state.GetEntityAtIndex(2));
            Assert.AreEqual(sequenceBefore + 1u, state.SequenceId);
            Assert.AreEqual(PartyLifecycleState.Active, state.State);
            Assert.AreEqual(2, state.MemberCount);
            Assert.AreNotEqual(PartyID.Uninitialized, newSolo);
            Assert.AreNotEqual(bSolo, newSolo);
            Assert.AreNotEqual(party, newSolo);
            Assert.AreEqual(newSolo, reg.GetPartyID(Ch(2)));
            Assert.IsTrue(reg.TryGetParty(newSolo, out PartyState soloState));
            Assert.IsTrue(soloState.IsSoloParty);
            Assert.AreEqual(En(2), soloState.GetEntityAtIndex(0));
        }

        [Test]
        public void test_registry_remove_one_of_two_active_members_stays_active_with_one_member()
        {
            // Pin only: whether a one-member Active party auto-disbands is Story 008 AC-3, not decided here.
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out _));

            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            Assert.AreEqual(PartyLifecycleState.Active, state.State);
            Assert.AreEqual(1, state.MemberCount);
            Assert.IsFalse(state.IsFreed);
        }

        [Test]
        public void test_registry_remove_all_members_party_freed_and_ids_not_reused()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState partyState));
            var seen = new List<uint> { party.RawValue };

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(1), out PartyID soloA));
            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out PartyID soloB));
            seen.Add(soloA.RawValue);
            seen.Add(soloB.RawValue);

            Assert.IsFalse(reg.TryGetParty(party, out _));
            AssertFreed(partyState);
            Assert.AreEqual(0, reg.GetPartyMembers(party).Count);
            PartyID next = Register(reg, 3);
            foreach (uint id in seen)
            {
                Assert.Greater(next.RawValue, id);
            }
        }

        [Test]
        public void test_registry_remove_leader_of_one_member_party_party_freed()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            Assert.IsTrue(reg.TryGetParty(party, out PartyState partyState));

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(1), out PartyID solo));

            Assert.IsFalse(reg.TryGetParty(party, out _));
            AssertFreed(partyState);
            Assert.AreEqual(solo, reg.GetPartyID(Ch(1)));
            Assert.AreEqual(1, reg.PartyCount);
        }

        // ---- AC-5 ----

        [Test]
        public void test_registry_members_order_preserved_across_add_and_remove()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 4);
            AssertMembers(reg, party, 1, 2, 3, 4);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(1), out _));
            AssertMembers(reg, party, 2, 3, 4);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            Assert.AreEqual(En(2), state.GetEntityAtIndex(0));
            Assert.AreEqual(En(3), state.GetEntityAtIndex(1));
            Assert.AreEqual(En(4), state.GetEntityAtIndex(2));
            Assert.AreEqual(EntityID.Invalid, state.GetEntityAtIndex(3));

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(1)));
            AssertMembers(reg, party, 2, 3, 4, 1);
            Assert.AreEqual(En(1), state.GetEntityAtIndex(3));
        }

        [Test]
        public void test_registry_snapshot_before_join_unchanged_and_new_list_is_different_reference()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            Register(reg, 3);
            IReadOnlyList<CharacterID> before = reg.GetPartyMembers(party);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(3)));
            IReadOnlyList<CharacterID> after = reg.GetPartyMembers(party);

            Assert.AreEqual(2, before.Count);
            Assert.AreEqual(Ch(1), before[0]);
            Assert.AreEqual(Ch(2), before[1]);
            Assert.AreEqual(3, after.Count);
            Assert.AreNotSame(before, after);
        }

        [Test]
        public void test_registry_snapshot_before_remove_unchanged_and_new_list_is_different_reference()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 3);
            IReadOnlyList<CharacterID> before = reg.GetPartyMembers(party);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.RemoveMember(party, Ch(2), out _));
            IReadOnlyList<CharacterID> after = reg.GetPartyMembers(party);

            Assert.AreEqual(3, before.Count);
            Assert.AreEqual(Ch(1), before[0]);
            Assert.AreEqual(Ch(2), before[1]);
            Assert.AreEqual(Ch(3), before[2]);
            Assert.AreEqual(2, after.Count);
            Assert.AreNotSame(before, after);
        }

        [Test]
        public void test_registry_snapshot_before_unregister_unchanged_and_new_list_is_different_reference()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 3);
            IReadOnlyList<CharacterID> before = reg.GetPartyMembers(party);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(2), out _));
            IReadOnlyList<CharacterID> after = reg.GetPartyMembers(party);

            Assert.AreEqual(3, before.Count);
            Assert.AreEqual(Ch(1), before[0]);
            Assert.AreEqual(Ch(2), before[1]);
            Assert.AreEqual(Ch(3), before[2]);
            Assert.AreEqual(2, after.Count);
            Assert.AreNotSame(before, after);
        }

        [Test]
        public void test_registry_snapshot_is_not_castable_to_mutable_array()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);

            IReadOnlyList<CharacterID> members = reg.GetPartyMembers(party);

            Assert.IsNull(members as CharacterID[]);
            var asList = members as IList<CharacterID>;
            Assert.IsNotNull(asList);
            Assert.IsTrue(asList.IsReadOnly);
        }

        [Test]
        public void test_registry_unknown_party_empty_list_same_instance()
        {
            PartyRegistry reg = NewRegistry();

            IReadOnlyList<CharacterID> first = reg.GetPartyMembers(new PartyID(UNKNOWN_PARTY_RAW));
            IReadOnlyList<CharacterID> second = reg.GetPartyMembers(PartyID.Uninitialized);

            Assert.AreEqual(0, first.Count);
            Assert.AreSame(first, second);
        }

        [Test]
        public void test_registry_freed_party_id_members_is_same_empty_instance_as_unknown_id()
        {
            PartyRegistry reg = NewRegistry();
            PartyID leaderSolo = Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out _));

            IReadOnlyList<CharacterID> freed = reg.GetPartyMembers(leaderSolo);
            IReadOnlyList<CharacterID> unknown = reg.GetPartyMembers(new PartyID(UNKNOWN_PARTY_RAW));

            Assert.AreEqual(0, freed.Count);
            Assert.AreSame(unknown, freed);
        }

        [Test]
        public void test_registry_get_member_at_index_bad_index_and_unknown_party_return_invalid()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);

            Assert.AreEqual(CharacterID.Invalid, reg.GetMemberAtIndex(party, -1));
            Assert.AreEqual(CharacterID.Invalid, reg.GetMemberAtIndex(party, 2));
            Assert.AreEqual(CharacterID.Invalid, reg.GetMemberAtIndex(party, int.MaxValue));
            Assert.AreEqual(CharacterID.Invalid, reg.GetMemberAtIndex(new PartyID(UNKNOWN_PARTY_RAW), 0));
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            Assert.AreEqual(EntityID.Invalid, state.GetEntityAtIndex(-1));
            Assert.AreEqual(EntityID.Invalid, state.GetEntityAtIndex(2));
        }

        [Test]
        public void test_registry_get_party_id_of_unknown_character_uninitialized()
        {
            PartyRegistry reg = NewRegistry();

            Assert.AreEqual(PartyID.Uninitialized, reg.GetPartyID(Ch(1)));
            Assert.AreEqual(PartyID.Uninitialized, reg.GetPartyID(CharacterID.Invalid));
            Assert.IsFalse(reg.TryGetParty(new PartyID(1u), out PartyState state));
            Assert.IsNull(state);
        }

        [Test]
        public void test_registry_repeated_queries_allocate_zero_bytes()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 3);
            CharacterID probe = Ch(2);
            PartyID unknown = new PartyID(UNKNOWN_PARTY_RAW);
            CharacterID unknownCharacter = Ch(UNKNOWN_CHARACTER_RAW);
            int sink = 0;

            for (int i = 0; i < QUERY_ITERATIONS; i++)
            {
                sink += RunQueries(reg, party, probe, unknown, unknownCharacter);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < QUERY_ITERATIONS; i++)
            {
                sink += RunQueries(reg, party, probe, unknown, unknownCharacter);
            }

            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.AreEqual(0L, after - before);
            Assert.Greater(sink, 0);
        }

        private static int RunQueries(PartyRegistry reg, PartyID party, CharacterID probe, PartyID unknown,
            CharacterID unknownCharacter)
        {
            IReadOnlyList<CharacterID> members = reg.GetPartyMembers(party);
            int total = members.Count + reg.GetPartyMembers(unknown).Count;
            for (int i = 0; i < members.Count; i++)
            {
                total += members[i] == probe ? 1 : 0;
            }

            total += reg.GetPartyID(probe) == party ? 1 : 0;
            total += reg.GetPartyID(unknownCharacter) == PartyID.Uninitialized ? 1 : 0;
            total += reg.GetMemberAtIndex(party, 1) == probe ? 1 : 0;
            total += reg.GetMemberAtIndex(unknown, 0) == CharacterID.Invalid ? 1 : 0;
            total += reg.TryGetParty(party, out PartyState state) ? state.MemberCount : 0;
            total += reg.IsRegistered(probe) ? 1 : 0;
            return total;
        }

        // ---- AC-6 ----

        [Test]
        public void test_registry_register_invalid_character_invalid_character_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            RegistrySnapshot snap = Snap(reg, new[] { 1u });

            PartyRegistryResult result = reg.RegisterPlayer(CharacterID.Invalid, En(1), out PartyID solo);

            Assert.AreEqual(PartyRegistryResult.InvalidCharacter, result);
            Assert.AreEqual(PartyID.Uninitialized, solo);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_register_invalid_entity_invalid_entity_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID existing = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, existing);

            PartyRegistryResult result = reg.RegisterPlayer(Ch(2), EntityID.Invalid, out PartyID solo);

            Assert.AreEqual(PartyRegistryResult.InvalidEntity, result);
            Assert.AreEqual(PartyID.Uninitialized, solo);
            snap.AssertUnchanged(reg);
            Assert.IsFalse(reg.IsRegistered(Ch(2)));
        }

        [Test]
        public void test_registry_register_twice_already_registered_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID first = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u }, first);

            PartyRegistryResult result = reg.RegisterPlayer(Ch(1), En(2), out PartyID second);

            Assert.AreEqual(PartyRegistryResult.AlreadyRegistered, result);
            Assert.AreEqual(PartyID.Uninitialized, second);
            snap.AssertUnchanged(reg);
            Assert.IsTrue(reg.TryGetParty(first, out PartyState state));
            Assert.AreEqual(En(1), state.GetEntityAtIndex(0));
        }

        [Test]
        public void test_registry_register_unregister_register_again_new_id_and_new_entity()
        {
            PartyRegistry reg = NewRegistry();
            PartyID first = Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(1), out _));

            PartyRegistryResult result = reg.RegisterPlayer(Ch(1), En(2), out PartyID second);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(reg.TryGetParty(first, out _), "the first solo id must not resolve");
            Assert.IsTrue(reg.TryGetParty(second, out PartyState state));
            Assert.AreEqual(En(2), state.GetEntityAtIndex(0));
            Assert.AreEqual(second, reg.GetPartyID(Ch(1)));
        }

        [Test]
        public void test_registry_unregister_solo_id_gone_and_not_registered()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);

            PartyRegistryResult result = reg.UnregisterPlayer(Ch(1), out PartyID former);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.AreEqual(PartyID.Uninitialized, former);
            Assert.IsFalse(reg.TryGetParty(solo, out _));
            Assert.IsFalse(reg.IsRegistered(Ch(1)));
            Assert.AreEqual(0, reg.PartyCount);
        }

        [Test]
        public void test_registry_unregister_unknown_not_registered_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, UNKNOWN_CHARACTER_RAW }, solo);

            PartyRegistryResult result = reg.UnregisterPlayer(Ch(UNKNOWN_CHARACTER_RAW), out PartyID former);

            Assert.AreEqual(PartyRegistryResult.NotRegistered, result);
            Assert.AreEqual(PartyID.Uninitialized, former);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_unregister_invalid_character_not_registered_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);

            PartyRegistryResult result = reg.UnregisterPlayer(CharacterID.Invalid, out PartyID former);

            Assert.AreEqual(PartyRegistryResult.NotRegistered, result);
            Assert.AreEqual(PartyID.Uninitialized, former);
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_unregister_member_of_three_compacts_reports_party_no_solo()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 3);
            int countBefore = reg.PartyCount;

            PartyRegistryResult result = reg.UnregisterPlayer(Ch(2), out PartyID former);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.AreEqual(party, former);
            AssertMembers(reg, party, 1, 3);
            Assert.AreEqual(PartyID.Uninitialized, reg.GetPartyID(Ch(2)));
            Assert.IsFalse(reg.IsRegistered(Ch(2)));
            Assert.AreEqual(countBefore, reg.PartyCount, "no new solo party for the leaver");
        }

        [Test]
        public void test_registry_unregister_leader_of_three_compacts_and_keeps_others_mapped()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 3);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            uint sequenceBefore = state.SequenceId;

            PartyRegistryResult result = reg.UnregisterPlayer(Ch(1), out PartyID former);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.AreEqual(party, former);
            AssertMembers(reg, party, 2, 3);
            Assert.AreEqual(En(2), state.GetEntityAtIndex(0));
            Assert.AreEqual(En(3), state.GetEntityAtIndex(1));
            Assert.AreEqual(EntityID.Invalid, state.GetEntityAtIndex(2));
            Assert.AreEqual(sequenceBefore + 1u, state.SequenceId);
            Assert.IsFalse(reg.IsRegistered(Ch(1)));
            Assert.AreEqual(party, reg.GetPartyID(Ch(2)));
            Assert.AreEqual(party, reg.GetPartyID(Ch(3)));
        }

        [Test]
        public void test_registry_unregister_last_member_of_party_party_freed()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            Assert.IsTrue(reg.TryGetParty(party, out PartyState partyState));

            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(1), out PartyID former));

            Assert.AreEqual(party, former);
            Assert.IsFalse(reg.TryGetParty(party, out _));
            AssertFreed(partyState);
            Assert.IsFalse(reg.IsRegistered(Ch(1)));
            Assert.AreEqual(0, reg.PartyCount);
        }

        [Test]
        public void test_registry_disband_two_member_party_both_solo_with_new_ordered_ids()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState partyState));

            PartyRegistryResult result = reg.DisbandParty(party);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.IsFalse(reg.TryGetParty(party, out _));
            AssertFreed(partyState);
            PartyID soloA = reg.GetPartyID(Ch(1));
            PartyID soloB = reg.GetPartyID(Ch(2));
            Assert.AreNotEqual(PartyID.Uninitialized, soloA);
            Assert.AreNotEqual(PartyID.Uninitialized, soloB);
            Assert.Less(soloA.RawValue, soloB.RawValue, "solo ids follow join order");
            Assert.Greater(soloA.RawValue, party.RawValue);
            Assert.Greater(soloB.RawValue, party.RawValue);
            Assert.IsTrue(reg.TryGetParty(soloA, out PartyState stateA));
            Assert.IsTrue(stateA.IsSoloParty);
            Assert.AreEqual(PartyLifecycleState.Active, stateA.State);
            Assert.AreEqual(En(1), stateA.GetEntityAtIndex(0));
            Assert.IsTrue(reg.TryGetParty(soloB, out PartyState stateB));
            Assert.IsTrue(stateB.IsSoloParty);
            Assert.AreEqual(PartyLifecycleState.Active, stateB.State);
            Assert.AreEqual(En(2), stateB.GetEntityAtIndex(0));
            Assert.AreEqual(2, reg.PartyCount);
        }

        [Test]
        public void test_registry_disband_unknown_or_solo_typed_failures_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID solo = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u }, solo);

            Assert.AreEqual(PartyRegistryResult.UnknownParty, reg.DisbandParty(new PartyID(UNKNOWN_PARTY_RAW)));
            snap.AssertUnchanged(reg);
            Assert.AreEqual(PartyRegistryResult.SoloParty, reg.DisbandParty(solo));
            snap.AssertUnchanged(reg);
        }

        [Test]
        public void test_registry_counter_at_max_register_refused_one_error_no_id_zero()
        {
            PartyRegistry reg = NewRegistry();
            reg.SetNextPartyIdForTests(uint.MaxValue);
            RegistrySnapshot snap = Snap(reg, new[] { 1u });
            LogAssert.Expect(LogType.Error, new Regex(EXHAUSTED_PATTERN));

            PartyRegistryResult result = reg.RegisterPlayer(Ch(1), En(1), out PartyID solo);

            Assert.AreEqual(PartyRegistryResult.IdsExhausted, result);
            Assert.AreEqual(PartyID.Uninitialized, solo);
            Assert.IsFalse(reg.IsRegistered(Ch(1)));
            snap.AssertUnchanged(reg);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_counter_one_below_max_issues_one_id_then_refuses()
        {
            PartyRegistry reg = NewRegistry();
            reg.SetNextPartyIdForTests(uint.MaxValue - 1u);

            PartyID first = Register(reg, 1);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, first);
            LogAssert.Expect(LogType.Error, new Regex(EXHAUSTED_PATTERN));
            PartyRegistryResult second = reg.RegisterPlayer(Ch(2), En(2), out PartyID secondId);

            Assert.AreEqual(uint.MaxValue - 1u, first.RawValue);
            Assert.AreEqual(PartyRegistryResult.IdsExhausted, second);
            Assert.AreEqual(PartyID.Uninitialized, secondId);
            snap.AssertUnchanged(reg);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_create_and_remove_with_no_ids_left_exhausted_state_unchanged()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            PartyID solo3 = Register(reg, 3);
            reg.SetNextPartyIdForTests(uint.MaxValue);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u, 3u }, party, solo3);

            LogAssert.Expect(LogType.Error, new Regex(EXHAUSTED_PATTERN));
            Assert.AreEqual(PartyRegistryResult.IdsExhausted, reg.TryCreateParty(Ch(3), out PartyID created));
            LogAssert.Expect(LogType.Error, new Regex(EXHAUSTED_PATTERN));
            Assert.AreEqual(PartyRegistryResult.IdsExhausted, reg.RemoveMember(party, Ch(2), out PartyID removedSolo));

            Assert.AreEqual(PartyID.Uninitialized, created);
            Assert.AreEqual(PartyID.Uninitialized, removedSolo);
            snap.AssertUnchanged(reg);
            AssertMembers(reg, party, 1, 2);
            Assert.AreEqual(solo3, reg.GetPartyID(Ch(3)));
            Assert.IsTrue(reg.TryGetParty(solo3, out _));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_disband_needs_two_ids_with_one_left_exhausted_party_intact()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            reg.SetNextPartyIdForTests(uint.MaxValue - 1u);
            RegistrySnapshot snap = Snap(reg, new[] { 1u, 2u }, party);
            LogAssert.Expect(LogType.Error, new Regex(EXHAUSTED_PATTERN));

            PartyRegistryResult result = reg.DisbandParty(party);

            Assert.AreEqual(PartyRegistryResult.IdsExhausted, result);
            snap.AssertUnchanged(reg);
            Assert.IsTrue(reg.TryGetParty(party, out PartyState state));
            Assert.AreEqual(Ch(1), state.GetMemberAtIndex(0));
            Assert.AreEqual(Ch(2), state.GetMemberAtIndex(1));
            Assert.AreEqual(En(1), state.GetEntityAtIndex(0));
            Assert.AreEqual(En(2), state.GetEntityAtIndex(1));
            Assert.AreEqual(party, reg.GetPartyID(Ch(1)));
            Assert.AreEqual(party, reg.GetPartyID(Ch(2)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_disband_with_exactly_two_ids_left_succeeds_without_log()
        {
            PartyRegistry reg = NewRegistry();
            PartyID party = BuildParty(reg, 2);
            reg.SetNextPartyIdForTests(uint.MaxValue - 2u);

            PartyRegistryResult result = reg.DisbandParty(party);

            Assert.AreEqual(PartyRegistryResult.Ok, result);
            Assert.IsFalse(reg.TryGetParty(party, out _));
            Assert.AreEqual(uint.MaxValue - 2u, reg.GetPartyID(Ch(1)).RawValue);
            Assert.AreEqual(uint.MaxValue - 1u, reg.GetPartyID(Ch(2)).RawValue);
            Assert.AreEqual(2, reg.PartyCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_counter_at_max_add_member_and_unregister_still_succeed_without_log()
        {
            PartyRegistry reg = NewRegistry();
            Register(reg, 1);
            Register(reg, 2);
            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryCreateParty(Ch(1), out PartyID party));
            Register(reg, 3);
            reg.SetNextPartyIdForTests(uint.MaxValue);

            Assert.AreEqual(PartyRegistryResult.Ok, reg.TryAddMember(party, Ch(2)));
            Assert.AreEqual(PartyRegistryResult.Ok, reg.UnregisterPlayer(Ch(3), out _));

            AssertMembers(reg, party, 1, 2);
            Assert.IsFalse(reg.IsRegistered(Ch(3)));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void test_registry_set_next_party_id_zero_throws_argument_out_of_range()
        {
            PartyRegistry reg = NewRegistry();

            Assert.Throws<ArgumentOutOfRangeException>(() => reg.SetNextPartyIdForTests(0u));
        }

        [Test]
        public void test_registry_invalid_config_constructor_throws_argument_exception()
        {
            // default(PartySystemConfigValues) has every knob 0: five range errors plus the retry-below-timeout rule.
            LogAssert.Expect(LogType.Error, new Regex(@"MAX_PARTY_SIZE = 0 is outside"));
            LogAssert.Expect(LogType.Error, new Regex(@"INVITE_TIMEOUT_SECONDS = 0 is outside"));
            LogAssert.Expect(LogType.Error, new Regex(@"INVITE_RETRY_SECONDS = 0 is outside"));
            LogAssert.Expect(LogType.Error, new Regex(@"RECONNECT_GRACE_SECONDS = 0 is outside"));
            LogAssert.Expect(LogType.Error, new Regex(@"DISBAND_TIMEOUT_SECONDS = 0 is outside"));
            LogAssert.Expect(LogType.Error,
                new Regex(@"INVITE_RETRY_SECONDS = 0 must be below INVITE_TIMEOUT_SECONDS = 0"));

            Assert.Throws<ArgumentException>(() => new PartyRegistry(default(PartySystemConfigValues)));

            LogAssert.NoUnexpectedReceived();
        }
    }
}
