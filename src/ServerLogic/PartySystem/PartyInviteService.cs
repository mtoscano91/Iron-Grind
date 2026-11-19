using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The party invite state machine (design/gdd/party-system.md CR-PS-3, CR-PS-4; TR-party-003, TR-party-004;
    /// Party System Story 007; ADR-014). One pending invite per recipient, a bounded FIFO queue of further
    /// inviters per recipient, one retry, expiry, and accept-time re-validation. A party is created only when an
    /// invite is accepted (I2). Timers are server ticks passed in by the caller; no wall clock.
    /// </summary>
    /// <remarks>
    /// Allocation: pending state lives in <see cref="RecipientInvites"/> objects pooled up front and keyed by the
    /// recipient in a dictionary created with the pool's capacity; queue arrays are fixed at
    /// <c>MaxPartySize * 2</c>. In steady state (pool not exhausted) no request allocates. A recipient count above
    /// the initial pool capacity allocates one more entry once, then reuses it. Tick thread only, not thread-safe.
    /// Disconnect cancellation is Story 009; until then invites expire on the timer. The caller must call
    /// <see cref="Tick"/> once per server tick (Story 011 wires it).
    /// </remarks>
    public sealed class PartyInviteService
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyInvite] ";

        private const int INITIAL_RECIPIENT_CAPACITY = 32;
        private const int QUEUE_CAPACITY_PER_PARTY_SLOT = 2;
        private const int MAX_LEVEL_BYTE = 255;

        private enum InviterRole
        {
            NotRegistered,
            Solo,
            Leader,
            Member,
        }

        private sealed class RecipientInvites
        {
            public CharacterID Inviter;
            public PartyInviteReceived Message;
            public uint ExpiryTick;
            public uint RetryTick;
            public bool RetrySent;
            public readonly CharacterID[] Queue;
            public int QueueCount;

            public RecipientInvites(int queueCapacity)
            {
                Queue = new CharacterID[queueCapacity];
            }
        }

        private readonly IPartyInviteMessageSink _sink;
        private readonly PartyRegistry _registry;
        private readonly IPartyMemberLevelSource _levels;
        private readonly ICharacterNameLookup _names;
        private readonly int _gate;
        private readonly uint _timeoutTicks;
        private readonly uint _retryTicks;
        private readonly int _queueCapacity;
        private readonly Dictionary<CharacterID, RecipientInvites> _pending;
        private readonly Stack<RecipientInvites> _pool;
        private readonly List<CharacterID> _scratch;
        private uint _lastTick;

        /// <summary>Creates the service.</summary>
        /// <param name="sink">Outbound messages.</param>
        /// <param name="registry">Membership and leader reads and the join writes.</param>
        /// <param name="levels">Level reads.</param>
        /// <param name="names">Inviter name lookup (a missing name is sent as empty).</param>
        /// <param name="config">The party knobs; must pass <c>Validate()</c>.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentException">The config failed validation.</exception>
        public PartyInviteService(IPartyInviteMessageSink sink, PartyRegistry registry, IPartyMemberLevelSource levels,
            ICharacterNameLookup names, PartySystemConfigValues config)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
            _names = names ?? throw new ArgumentNullException(nameof(names));
            if (!config.Validate())
            {
                throw new ArgumentException("The party system config failed validation (see the logged errors).",
                    nameof(config));
            }

            _gate = config.LevelRangeGate;
            _timeoutTicks = (uint)config.InviteTimeoutTicks;
            _retryTicks = (uint)config.InviteRetryTicks;
            _queueCapacity = config.MaxPartySize * QUEUE_CAPACITY_PER_PARTY_SLOT;
            _pending = new Dictionary<CharacterID, RecipientInvites>(INITIAL_RECIPIENT_CAPACITY);
            _pool = new Stack<RecipientInvites>(INITIAL_RECIPIENT_CAPACITY);
            _scratch = new List<CharacterID>(INITIAL_RECIPIENT_CAPACITY);
            for (int i = 0; i < INITIAL_RECIPIENT_CAPACITY; i++)
            {
                _pool.Push(new RecipientInvites(_queueCapacity));
            }
        }

        /// <summary>The number of recipients with a pending invite.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>The number of invites queued behind a recipient's pending one (0 when none pending).</summary>
        /// <param name="target">The recipient.</param>
        /// <returns>The queued count.</returns>
        public int GetQueuedCount(CharacterID target)
        {
            return _pending.TryGetValue(target, out RecipientInvites entry) ? entry.QueueCount : 0;
        }

        /// <summary>Whether a recipient has a pending (delivered) invite.</summary>
        /// <param name="target">The recipient.</param>
        /// <returns>True when pending.</returns>
        public bool HasPending(CharacterID target)
        {
            return _pending.ContainsKey(target);
        }

        /// <summary>
        /// Removes every pending or queued invite where <paramref name="character"/> is the target or the inviter
        /// (Story 009 AC-7: the player became Ghost or its slot was released). A connected inviter whose target
        /// vanished gets one <c>Expired</c> naming the target; a target whose inviter vanished is told nothing.
        /// </summary>
        /// <remarks>
        /// Queue choice: when a vanished inviter's pending entry is removed and the target has queued invites, the
        /// existing promotion logic runs (the next queued invite that still validates is delivered). Its timers use
        /// the latest tick this service saw (<c>HandleInvite</c>, <c>HandleResponse</c> or <c>Tick</c>). When the
        /// target vanished, the pending inviter and every queued inviter get the <c>Expired</c> notice.
        /// Call it while the character is still registered or after; no allocation in steady state.
        /// </remarks>
        /// <param name="character">The character that vanished.</param>
        public void CancelFor(CharacterID character)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            if (_pending.TryGetValue(character, out RecipientInvites own))
            {
                NotifyExpired(own.Inviter, character);
                for (int i = 0; i < own.QueueCount; i++)
                {
                    NotifyExpired(own.Queue[i], character);
                }

                Array.Clear(own.Queue, 0, own.Queue.Length);
                own.QueueCount = 0;
                own.Inviter = CharacterID.Invalid;
                _pending.Remove(character);
                _pool.Push(own);
            }

            _scratch.Clear();
            foreach (KeyValuePair<CharacterID, RecipientInvites> pair in _pending)
            {
                _scratch.Add(pair.Key);
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                CharacterID target = _scratch[i];
                RecipientInvites entry = _pending[target];
                RemoveFromQueue(entry, character);
                if (entry.Inviter == character)
                {
                    Resolve(target, entry, _lastTick);
                }
            }
        }

        private void NotifyExpired(CharacterID inviter, CharacterID target)
        {
            if (inviter != target && _registry.IsRegistered(inviter) && _registry.IsMemberConnected(inviter))
            {
                _sink.SendInviteResult(inviter, PartyInviteResultCode.Expired, target);
            }
        }

        private static void RemoveFromQueue(RecipientInvites entry, CharacterID character)
        {
            int write = 0;
            for (int read = 0; read < entry.QueueCount; read++)
            {
                if (entry.Queue[read] != character)
                {
                    entry.Queue[write++] = entry.Queue[read];
                }
            }

            for (int i = write; i < entry.QueueCount; i++)
            {
                entry.Queue[i] = CharacterID.Invalid;
            }

            entry.QueueCount = write;
        }

        /// <summary>Handles a <c>PartyInviteRequest</c> from <paramref name="senderEntity"/> (AC-1, AC-2, AC-6, AC-7).</summary>
        /// <param name="senderEntity">The envelope's sender entity.</param>
        /// <param name="target">The invited character.</param>
        /// <param name="currentTick">The dispatch tick.</param>
        public void HandleInvite(EntityID senderEntity, CharacterID target, uint currentTick)
        {
            _lastTick = currentTick;
            if (!_registry.TryGetCharacter(senderEntity, out CharacterID sender))
            {
                return;
            }

            PartyInviteResultCode code = Validate(sender, target);
            if (code != PartyInviteResultCode.Sent)
            {
                _sink.SendInviteResult(sender, code, target);
                return;
            }

            _sink.SendInviteResult(sender, EnqueueOrRepeat(sender, target, currentTick), target);
        }

        /// <summary>
        /// A validated invite: delivers it when the recipient has none pending (Sent), repeats the answer for a
        /// repeat sender (Sent or Queued), queues a new sender (Queued) or declines it when the queue is full.
        /// </summary>
        private PartyInviteResultCode EnqueueOrRepeat(CharacterID sender, CharacterID target, uint currentTick)
        {
            if (!_pending.TryGetValue(target, out RecipientInvites entry))
            {
                entry = Rent();
                _pending.Add(target, entry);
                Deliver(target, entry, sender, currentTick);
                return PartyInviteResultCode.Sent;
            }

            if (entry.Inviter == sender)
            {
                return PartyInviteResultCode.Sent;
            }

            for (int i = 0; i < entry.QueueCount; i++)
            {
                if (entry.Queue[i] == sender)
                {
                    return PartyInviteResultCode.Queued;
                }
            }

            if (entry.QueueCount >= _queueCapacity)
            {
                Debug.LogWarning(LOG_PREFIX + "invite queue full for " + target + "; declining " + sender + ".");
                return PartyInviteResultCode.Declined;
            }

            entry.Queue[entry.QueueCount++] = sender;
            return PartyInviteResultCode.Queued;
        }

        /// <summary>Handles a <c>PartyInviteResponse</c> from <paramref name="responderEntity"/> (AC-3, AC-4, AC-7).</summary>
        /// <param name="responderEntity">The envelope's sender entity (the invited character).</param>
        /// <param name="inviter">The inviter the response names.</param>
        /// <param name="accepted">True to accept, false to decline.</param>
        /// <param name="currentTick">The dispatch tick.</param>
        public void HandleResponse(EntityID responderEntity, CharacterID inviter, bool accepted, uint currentTick)
        {
            _lastTick = currentTick;
            if (!_registry.TryGetCharacter(responderEntity, out CharacterID target)
                || !_pending.TryGetValue(target, out RecipientInvites entry)
                || entry.Inviter != inviter)
            {
                Debug.LogWarning(LOG_PREFIX + "response from entity " + responderEntity + " for inviter " + inviter
                    + " matches no pending invite; ignored.");
                return;
            }

            if (!accepted)
            {
                _sink.SendInviteResult(inviter, PartyInviteResultCode.Declined, target);
                Resolve(target, entry, currentTick);
                return;
            }

            PartyInviteResultCode code = Validate(inviter, target);
            if (code == PartyInviteResultCode.NotLeader || code == PartyInviteResultCode.Expired)
            {
                code = PartyInviteResultCode.Expired;
            }

            if (code != PartyInviteResultCode.Sent)
            {
                _sink.SendInviteResult(target, code, inviter);
            }
            else
            {
                PartyRegistryResult joined = Join(inviter, target);
                if (joined != PartyRegistryResult.Ok)
                {
                    // The accept passed validation but the registry refused the join (for example IdsExhausted):
                    // tell the target the invite is no longer valid; the slot is still resolved below.
                    Debug.LogError(LOG_PREFIX + "accepted invite " + inviter + " -> " + target
                        + " could not join: " + joined + ".");
                    _sink.SendInviteResult(target, PartyInviteResultCode.Expired, inviter);
                }
            }

            Resolve(target, entry, currentTick);
        }

        /// <summary>Advances the timers: one retry at delivery + retry ticks, expiry at delivery + timeout ticks.</summary>
        /// <param name="currentTick">The current server tick.</param>
        public void Tick(uint currentTick)
        {
            _lastTick = currentTick;
            if (_pending.Count == 0)
            {
                return;
            }

            _scratch.Clear();
            foreach (KeyValuePair<CharacterID, RecipientInvites> pair in _pending)
            {
                _scratch.Add(pair.Key);
            }

            for (int i = 0; i < _scratch.Count; i++)
            {
                CharacterID target = _scratch[i];
                if (!_pending.TryGetValue(target, out RecipientInvites entry))
                {
                    continue;
                }

                if (Reached(currentTick, entry.ExpiryTick))
                {
                    _sink.SendInviteResult(entry.Inviter, PartyInviteResultCode.Expired, target);
                    Resolve(target, entry, currentTick);
                }
                else if (!entry.RetrySent && Reached(currentTick, entry.RetryTick))
                {
                    entry.RetrySent = true;
                    _sink.SendInviteReceived(target, in entry.Message);
                }
            }
        }

        private static bool Reached(uint now, uint due)
        {
            return unchecked((int)(now - due)) >= 0;
        }

        private RecipientInvites Rent()
        {
            RecipientInvites entry = _pool.Count > 0 ? _pool.Pop() : new RecipientInvites(_queueCapacity);
            entry.QueueCount = 0;
            return entry;
        }

        private void Deliver(CharacterID target, RecipientInvites entry, CharacterID inviter, uint tick)
        {
            string name = _names.TryGetCharacterName(inviter, out string found) && found != null ? found : string.Empty;
            uint inviterEntityRaw = 0u;
            int level = 0;
            if (_registry.TryGetEntity(inviter, out EntityID inviterEntity))
            {
                inviterEntityRaw = inviterEntity.RawValue;
                _levels.TryGetLevel(inviterEntity, out level);
            }

            entry.Inviter = inviter;
            entry.ExpiryTick = unchecked(tick + _timeoutTicks);
            entry.RetryTick = unchecked(tick + _retryTicks);
            entry.RetrySent = false;
            entry.Message = new PartyInviteReceived(inviter.RawValue, inviterEntityRaw, name,
                (byte)Mathf.Clamp(level, 0, MAX_LEVEL_BYTE), entry.ExpiryTick);
            _sink.SendInviteReceived(target, in entry.Message);
        }

        /// <summary>Frees the pending slot, then delivers the next queued invite that still validates.</summary>
        private void Resolve(CharacterID target, RecipientInvites entry, uint tick)
        {
            while (entry.QueueCount > 0)
            {
                CharacterID next = entry.Queue[0];
                for (int i = 1; i < entry.QueueCount; i++)
                {
                    entry.Queue[i - 1] = entry.Queue[i];
                }

                entry.QueueCount--;
                entry.Queue[entry.QueueCount] = CharacterID.Invalid;
                PartyInviteResultCode code = Validate(next, target);
                if (code == PartyInviteResultCode.Sent)
                {
                    Deliver(target, entry, next, tick);
                    return;
                }

                _sink.SendInviteResult(next, code, target);
            }

            entry.Inviter = CharacterID.Invalid;
            _pending.Remove(target);
            _pool.Push(entry);
        }

        private PartyRegistryResult Join(CharacterID inviter, CharacterID target)
        {
            PartyID partyId = _registry.GetPartyID(inviter);
            if (_registry.TryGetParty(partyId, out PartyState party) && party.IsSoloParty)
            {
                PartyRegistryResult created = _registry.TryCreateParty(inviter, out partyId);
                if (created != PartyRegistryResult.Ok)
                {
                    return created;
                }
            }

            return _registry.TryAddMember(partyId, target);
        }

        private InviterRole ResolveInviter(CharacterID inviter, out PartyState party, out EntityID leaderEntity,
            out CharacterID leader)
        {
            party = null;
            leaderEntity = EntityID.Invalid;
            leader = inviter;
            if (!_registry.TryGetEntity(inviter, out EntityID inviterEntity)
                || !_registry.TryGetParty(_registry.GetPartyID(inviter), out PartyState state))
            {
                return InviterRole.NotRegistered;
            }

            if (state.IsSoloParty)
            {
                leaderEntity = inviterEntity;
                return InviterRole.Solo;
            }

            party = state;
            leader = state.GetMemberAtIndex(state.LeaderIndex);
            leaderEntity = state.GetEntityAtIndex(state.LeaderIndex);
            return leader == inviter ? InviterRole.Leader : InviterRole.Member;
        }

        /// <summary>
        /// The AC-1 rules, shared by send, queue delivery and accept. Returns Sent when valid, NotLeader for a
        /// non-leader member, Expired for an unregistered inviter, else the rejection code.
        /// </summary>
        private PartyInviteResultCode Validate(CharacterID inviter, CharacterID target)
        {
            InviterRole role = ResolveInviter(inviter, out PartyState party, out EntityID leaderEntity, out _);
            PartyInviteResultCode roleCode = ValidateInviter(role, party);
            return roleCode != PartyInviteResultCode.Sent
                ? roleCode
                : ValidateTargetAndLevels(inviter, target, party, leaderEntity);
        }

        /// <summary>
        /// The inviter role checks: an unregistered inviter is Expired, a non-leader member NotLeader, and so is
        /// any inviter whose party is Disbanding (Story 008, L2); an accept into it is then answered Expired.
        /// </summary>
        private static PartyInviteResultCode ValidateInviter(InviterRole role, PartyState party)
        {
            if (role == InviterRole.NotRegistered)
            {
                return PartyInviteResultCode.Expired;
            }

            bool disbanding = party != null && party.State == PartyLifecycleState.Disbanding;
            return role == InviterRole.Member || disbanding
                ? PartyInviteResultCode.NotLeader
                : PartyInviteResultCode.Sent;
        }

        /// <summary>
        /// Target existence, AlreadyInParty, PartyFull then level range, in that order. A target whose level cannot
        /// be read maps to TargetNotFound (rare: a registered character the level source does not know); at accept
        /// time that code reaches the target, who is told the invite is no longer valid.
        /// </summary>
        private PartyInviteResultCode ValidateTargetAndLevels(CharacterID inviter, CharacterID target,
            PartyState party, EntityID leaderEntity)
        {
            if (!_registry.TryGetEntity(target, out EntityID targetEntity))
            {
                return PartyInviteResultCode.TargetNotFound;
            }

            if (target == inviter)
            {
                return PartyInviteResultCode.AlreadyInParty;
            }

            if (_registry.TryGetParty(_registry.GetPartyID(target), out PartyState targetParty)
                && !targetParty.IsSoloParty)
            {
                return PartyInviteResultCode.AlreadyInParty;
            }

            if (party != null && party.MemberCount >= _registry.MaxPartySize)
            {
                return PartyInviteResultCode.PartyFull;
            }

            if (!_levels.TryGetLevel(targetEntity, out int targetLevel))
            {
                return PartyInviteResultCode.TargetNotFound;
            }

            if (!_levels.TryGetLevel(leaderEntity, out int leaderLevel) || Math.Abs(targetLevel - leaderLevel) > _gate)
            {
                return PartyInviteResultCode.LevelRange;
            }

            return PartyInviteResultCode.Sent;
        }
    }
}
