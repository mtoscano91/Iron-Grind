using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.Networking
{
    /// <summary>
    /// RFR-2 server-side relevance filter (<c>networking-relevance-filter.md</c>, Networking Core
    /// Story 028): builds the exact per-client, per-tick set of <see cref="EntityHealthUpdate"/>/
    /// <see cref="PartyMemberHealthUpdate"/> sub-messages that make up the two RFR-1 relevance sets
    /// (self+target-slot and party-set), wrapped as <see cref="PendingSubMessage"/> entries ready to
    /// pass into <see cref="RUBatchWriter.Write"/>'s <c>otherSubMessages</c> parameter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class</b>, mirroring <see cref="PartyDisbandCoordinator"/>'s and
    /// <see cref="MobDeTargetingCoordinator"/>'s shape: every call is fully described by its
    /// arguments — nothing is tracked between calls. This class does not call
    /// <see cref="RUBatchWriter.Write"/> itself and <see cref="RUBatchWriter"/> is never modified by
    /// this story — the caller merges this method's return value with any other opaque
    /// <see cref="RUBatchCategory"/> sub-messages for the tick and passes the combined list to
    /// <see cref="RUBatchWriter.Write"/>. <see cref="RUBatchWriter"/> buckets <c>otherSubMessages</c>
    /// by <see cref="PendingSubMessage.Category"/> internally regardless of input order, so no
    /// special merge/ordering step is required here.
    /// </para>
    /// <para>
    /// <b>Mock party-membership input, per this story's own Out of Scope note:</b> no real Party
    /// System exists yet in this codebase. <paramref name="partyMembers"/> (in
    /// <see cref="BuildHealthUpdateSubMessages"/>) is supplied by the caller's own test-local or
    /// future-system-local party roster — the same forward-dependency treatment
    /// <see cref="PartyDisbandCoordinator"/> gives the not-yet-built Party System (see that class's
    /// own remarks: "a plain caller-supplied list, not an injected provider/service interface"). A
    /// future Party System story is expected to call this filter with a real membership list.
    /// </para>
    /// <para>
    /// <b><see cref="EntityHealthUpdate"/> reused for both self-slot and target-slot input:</b>
    /// <see cref="BuildHealthUpdateSubMessages"/> takes <c>selfSlot</c> and <c>target</c> both as
    /// <see cref="EntityHealthUpdate"/> values rather than separate <c>EntityID</c>/HP parameters —
    /// this type already carries exactly the fields RFR-1 needs for either slot, so no separate
    /// domain-only parameter set or "snapshot" type is introduced (the same
    /// struct-doubles-as-domain-value-and-wire-payload pattern <see cref="DamageEvent"/> and
    /// <see cref="GoldSyncEvent"/> already establish). <c>target.EntityId == </c><see cref="EntityID.Invalid"/>
    /// is the caller-facing "no target" sentinel, matching the <c>SetTarget</c> RPC's own wire
    /// convention (RFR-3a: "EntityID = 0 means deselect current target"). <see cref="RelevanceFilter"/>
    /// never serializes an <see cref="EntityID.Invalid"/> EntityID onto the wire — a "no target" input
    /// simply omits the target-slot sub-message entirely.
    /// </para>
    /// <para>
    /// <b>Separation invariant (RFR-1):</b> the target-slot <see cref="EntityHealthUpdate"/> is
    /// suppressed whenever <c>target.EntityId</c> is found among <paramref name="partyMembers"/>'
    /// <see cref="PartyMemberHealthUpdate.EntityId"/> values — that entity already receives a richer
    /// <see cref="PartyMemberHealthUpdate"/> instead. No entity ever appears in both sets in the same
    /// call.
    /// </para>
    /// <para>
    /// <b>Party size guard:</b> <paramref name="partyMembers"/> must not exceed
    /// <see cref="MaxPartyMembersExcludingSelf"/> (3) entries — a 4-person party (<see cref="MAX_PARTY_SIZE"/>)
    /// minus the client itself. A caller supplying more throws <see cref="ArgumentException"/>, matching
    /// this codebase's established defensive-guard convention for a caller-side invariant violation
    /// (e.g. <see cref="RUBatchWriter"/>'s misrouted-category throw, the Story 027 self-damage
    /// recipient mismatch guard).
    /// </para>
    /// <para>
    /// <b>Complexity:</b> O(1 + |partySet|) ≤ O(<see cref="MAX_PARTY_SIZE"/>) per call — the target
    /// party-membership check is a linear scan over the bounded (≤3-entry) <paramref name="partyMembers"/>
    /// list, never a scan over the full zone (RFR-2).
    /// </para>
    /// <para>
    /// <b>Test observation:</b> fires <see cref="INetworkTestObserver.OnRUBatchEntityHealthUpdates"/>
    /// exactly once per call, with the self-slot EntityID always present and the target-slot EntityID
    /// included only when actually appended (matching that hook's own documented scope — self-slot and
    /// target-slot only, never party members).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var selfSlot = new EntityHealthUpdate(new EntityID(1), currentHP: 750, maxHP: 1000);
    /// var target = new EntityHealthUpdate(new EntityID(99), currentHP: 300, maxHP: 300); // non-party
    /// var partyMembers = new[]
    /// {
    ///     new PartyMemberHealthUpdate(new EntityID(2), 500, 500, 100, 100),
    ///     new PartyMemberHealthUpdate(new EntityID(3), 400, 500, 80, 100),
    ///     new PartyMemberHealthUpdate(new EntityID(4), 500, 500, 100, 100),
    /// };
    ///
    /// List&lt;PendingSubMessage&gt; healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
    ///     clientId: 7u, in selfSlot, in target, partyMembers, observer);
    ///
    /// int bytesWritten = RUBatchWriter.Write(
    ///     buffer, sequenceNumber, tickNumber, damageEvents, goldSyncEvents,
    ///     otherSubMessages: healthUpdates, clientIdForLogging: 7u, observer);
    /// </code>
    /// </example>
    public static class RelevanceFilter
    {
        /// <summary>Full party size, including the client itself (RFR-1: "4-person party").</summary>
        public const int MAX_PARTY_SIZE = 4;

        /// <summary>
        /// Maximum number of <paramref name="partyMembers"/> entries <see cref="BuildHealthUpdateSubMessages"/>
        /// accepts: <see cref="MAX_PARTY_SIZE"/> minus the client itself (RFR-1's "3 (4-person party
        /// − self)"). Equal to <c>MAX_PMHU_PER_CLIENT</c> in the GDD's Tuning Knobs table.
        /// </summary>
        public const int MaxPartyMembersExcludingSelf = MAX_PARTY_SIZE - 1;

        /// <summary>
        /// Builds this tick's <see cref="EntityHealthUpdate"/>/<see cref="PartyMemberHealthUpdate"/>
        /// sub-messages for one client, per the RFR-2 algorithm: self-slot always, target-slot only if
        /// a non-party target is present, one <see cref="PartyMemberHealthUpdate"/> per party member.
        /// </summary>
        /// <param name="clientId">
        /// The destination client's ID — passed through to
        /// <see cref="INetworkTestObserver.OnRUBatchEntityHealthUpdates"/> and used nowhere else in
        /// this method (the wire payloads themselves carry no client identity, per RFR-1: both sets
        /// are server-side only and never transmitted to the client).
        /// </param>
        /// <param name="selfSlot">
        /// The client's own EntityID and current/max HP (RFR-5). <see cref="EntityHealthUpdate.EntityId"/>
        /// must not be <see cref="EntityID.Invalid"/> — <see cref="BatchSubMessageCodec.WriteEntityHealthUpdateBody"/>
        /// enforces this (CR-NET-7.3 zero-write guard) when the self-slot sub-message is built.
        /// </param>
        /// <param name="target">
        /// The client's current target, or an <see cref="EntityHealthUpdate"/> with
        /// <see cref="EntityHealthUpdate.EntityId"/> == <see cref="EntityID.Invalid"/> to mean "no
        /// target" (RFR-3a's own wire convention). Ignored entirely (never appended, never validated)
        /// when <see cref="EntityID.Invalid"/>.
        /// </param>
        /// <param name="partyMembers">
        /// The client's party members, excluding the client's own EntityID (RFR-1's "Party set").
        /// May be <see langword="null"/> (treated as empty — solo player). Must not exceed
        /// <see cref="MaxPartyMembersExcludingSelf"/> entries. This is a plain caller-supplied list —
        /// no Party System provider/service interface exists yet in this codebase (see class remarks).
        /// </param>
        /// <param name="observer">Optional test/dev-build observer. Fires <c>OnRUBatchEntityHealthUpdates</c> exactly once.</param>
        /// <returns>
        /// A new <see cref="List{T}"/> of <see cref="PendingSubMessage"/> entries — 1 or 2 tagged
        /// <see cref="RUBatchCategory.EntityHealthUpdate"/> followed by 0-<see cref="MaxPartyMembersExcludingSelf"/>
        /// tagged <see cref="RUBatchCategory.PartyMemberHealthUpdate"/> — ready to pass (optionally
        /// merged with other opaque categories) into <see cref="RUBatchWriter.Write"/>'s
        /// <c>otherSubMessages</c> parameter.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="partyMembers"/> has more than <see cref="MaxPartyMembersExcludingSelf"/> entries.
        /// </exception>
        /// <example>
        /// <code>
        /// List&lt;PendingSubMessage&gt; healthUpdates = RelevanceFilter.BuildHealthUpdateSubMessages(
        ///     clientId: 7u, in selfSlot, in target, partyMembers, observer);
        /// </code>
        /// </example>
        public static List<PendingSubMessage> BuildHealthUpdateSubMessages(
            uint clientId,
            in EntityHealthUpdate selfSlot,
            in EntityHealthUpdate target,
            IReadOnlyList<PartyMemberHealthUpdate> partyMembers
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            partyMembers ??= Array.Empty<PartyMemberHealthUpdate>();
            if (partyMembers.Count > MaxPartyMembersExcludingSelf)
            {
                throw new ArgumentException(
                    $"partyMembers.Count ({partyMembers.Count}) exceeds MaxPartyMembersExcludingSelf " +
                    $"({MaxPartyMembersExcludingSelf}) — a party can never exceed MAX_PARTY_SIZE ({MAX_PARTY_SIZE}) " +
                    "including the client itself (RFR-1).",
                    nameof(partyMembers));
            }

            var subMessages = new List<PendingSubMessage>(2 + partyMembers.Count);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            var deliveredEntityIds = new List<uint>(2) { selfSlot.EntityId.RawValue };
#endif

            // Self-slot: always present (RFR-5).
            AppendEntityHealthUpdate(subMessages, in selfSlot);

            // Target-slot: only if a target exists and is not a party member (RFR-1 separation invariant).
            bool hasTarget = target.EntityId != EntityID.Invalid;
            bool targetIsPartyMember = false;
            if (hasTarget)
            {
                for (int i = 0; i < partyMembers.Count; i++)
                {
                    if (partyMembers[i].EntityId == target.EntityId)
                    {
                        targetIsPartyMember = true;
                        break;
                    }
                }
            }

            if (hasTarget && !targetIsPartyMember)
            {
                AppendEntityHealthUpdate(subMessages, in target);
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
                deliveredEntityIds.Add(target.EntityId.RawValue);
#endif
            }

            // Party set: one PartyMemberHealthUpdate per member, unconditionally (RFR-2, O(|partySet|) <= O(3)).
            for (int i = 0; i < partyMembers.Count; i++)
            {
                PartyMemberHealthUpdate member = partyMembers[i];
                AppendPartyMemberHealthUpdate(subMessages, in member);
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnRUBatchEntityHealthUpdates(clientId, deliveredEntityIds);
#endif

            return subMessages;
        }

        private static void AppendEntityHealthUpdate(List<PendingSubMessage> subMessages, in EntityHealthUpdate entityHealthUpdate)
        {
            byte[] body = new byte[EntityHealthUpdate.BodySize];
            BatchSubMessageCodec.WriteEntityHealthUpdateBody(body, in entityHealthUpdate);
            subMessages.Add(new PendingSubMessage(RUBatchCategory.EntityHealthUpdate, EntityHealthUpdate.MessageTypeId, body));
        }

        private static void AppendPartyMemberHealthUpdate(List<PendingSubMessage> subMessages, in PartyMemberHealthUpdate partyMemberHealthUpdate)
        {
            byte[] body = new byte[PartyMemberHealthUpdate.BodySize];
            BatchSubMessageCodec.WritePartyMemberHealthUpdateBody(body, in partyMemberHealthUpdate);
            subMessages.Add(new PendingSubMessage(RUBatchCategory.PartyMemberHealthUpdate, PartyMemberHealthUpdate.MessageTypeId, body));
        }
    }
}
