using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using IronGrind.PartySystem;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// The party health stream (design/gdd/party-system.md Interactions, <c>PartyMemberHealthUpdate</c> bandwidth
    /// management; networking-wire-protocol.md 0x0305; TR-party-011; Party System Story 010, ADR-014). Once per tick
    /// <see cref="Run"/> sends each Online or Dead member of every Active real party an absolute HP / MP update about
    /// every other Online or Dead member, through the R-U batch, when the values moved enough since the last send.
    /// </summary>
    /// <remarks>
    /// Not an <c>IZoneOutboundStep</c>: Story 011 calls <see cref="Run"/> after the party poll and state broadcast and
    /// before the zone outbound step. A last-sent record is kept per (recipient, member) pair in storage sized at
    /// construction (<c>zoneCapacity x (MaxPartySize - 1)</c> pairs); a pair not considered in a <see cref="Run"/> is
    /// forgotten at its end, so the next eligible tick sends afresh. Steady state allocates nothing. Tick thread only.
    /// </remarks>
    public sealed class PartyHealthStreamStep
    {
        /// <summary>Log prefix.</summary>
        public const string LOG_PREFIX = "[PartyHealthStream] ";

        private const int INITIAL_PARTY_BUFFER = 16;
        private const int KEY_SHIFT = 32;

        // Large enough for any valid MaxPartySize (GetPartyMembersWithStatus needs at least MaxPartySize entries).
        private const int MEMBER_BUFFER_SIZE = PartySystemConfigValues.MAX_PARTY_SIZE_MAX;

        private readonly PartyRegistry _registry;
        private readonly IZoneEntityStatReader _stats;
        private readonly IPartyClientResolver _clients;
        private readonly IRUBatchCollector _collector;
        private readonly float _threshold;
        private readonly int _capacity;
        private readonly Dictionary<ulong, int> _index;
        private readonly ulong[] _keys;
        private readonly int[] _lastHp;
        private readonly int[] _lastMaxHp;
        private readonly int[] _lastMp;
        private readonly int[] _lastMaxMp;
        private readonly uint[] _stamp;
        private int _count;
        private PartyID[] _ids = new PartyID[INITIAL_PARTY_BUFFER];
        private bool _loggedError;
        private bool _warnedFull;

        /// <summary>Creates the step and pre-sizes the record storage.</summary>
        /// <param name="registry">Party and member reads.</param>
        /// <param name="stats">HP, MP and maxima of the members.</param>
        /// <param name="clients">Resolves a recipient to its connection.</param>
        /// <param name="collector">The R-U batch intake.</param>
        /// <param name="config">The Party System knobs; must pass <c>Validate()</c>.</param>
        /// <param name="zoneCapacity">The zone's player capacity (at least 1).</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="zoneCapacity"/> is below 1.</exception>
        /// <exception cref="ArgumentException">The config failed validation.</exception>
        public PartyHealthStreamStep(PartyRegistry registry, IZoneEntityStatReader stats, IPartyClientResolver clients,
            IRUBatchCollector collector, PartySystemConfigValues config, int zoneCapacity)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _clients = clients ?? throw new ArgumentNullException(nameof(clients));
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            if (zoneCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(zoneCapacity), zoneCapacity,
                    "zoneCapacity must be at least 1.");
            }

            if (!config.Validate())
            {
                throw new ArgumentException("The party system config failed validation (see the logged errors).",
                    nameof(config));
            }

            _threshold = config.HpDeltaThresholdFraction;
            _capacity = zoneCapacity * (config.MaxPartySize - 1);
            _index = new Dictionary<ulong, int>(_capacity);
            _keys = new ulong[_capacity];
            _lastHp = new int[_capacity];
            _lastMaxHp = new int[_capacity];
            _lastMp = new int[_capacity];
            _lastMaxMp = new int[_capacity];
            _stamp = new uint[_capacity];
        }

        /// <summary>The number of (recipient, member) records currently kept.</summary>
        public int RecordCount => _count;

        /// <summary>The most records the storage holds.</summary>
        public int RecordCapacity => _capacity;

        /// <summary>Sends this tick's health updates and drops records not considered this tick. Call once per tick.</summary>
        /// <remarks>
        /// A pair counts as considered only when its recipient has a client and the member's stats could be read;
        /// otherwise its record is dropped and the next eligible tick sends afresh. A second call with the same
        /// <paramref name="currentTick"/> keeps records from the first call (the stamps still match). One error is
        /// logged per call for a throwing resolver or stat read; other pairs still run.
        /// </remarks>
        /// <param name="currentTick">The server tick; used as the "seen this tick" stamp.</param>
        public void Run(uint currentTick)
        {
            _loggedError = false;
            _warnedFull = false;
            try
            {
                if (_registry.PartyCount > _ids.Length)
                {
                    _ids = new PartyID[_registry.PartyCount * 2];
                }

                int count = _registry.CopyRealPartyIds(_ids);
                for (int i = 0; i < count; i++)
                {
                    if (_registry.TryGetParty(_ids[i], out PartyState party)
                        && !party.IsSoloParty
                        && party.State == PartyLifecycleState.Active)
                    {
                        StreamParty(_ids[i], currentTick);
                    }
                }
            }
            finally
            {
                Sweep(currentTick);
            }
        }

        private void StreamParty(PartyID partyId, uint tick)
        {
            Span<PartyMemberStatusEntry> members = stackalloc PartyMemberStatusEntry[MEMBER_BUFFER_SIZE];
            int count = _registry.GetPartyMembersWithStatus(partyId, members);
            for (int r = 0; r < count; r++)
            {
                PartyMemberStatusEntry recipient = members[r];
                if (!IsEligible(recipient))
                {
                    continue;
                }

                for (int m = 0; m < count; m++)
                {
                    if (m == r || !IsEligible(members[m]))
                    {
                        continue;
                    }

                    ProcessPair(recipient, members[m], tick);
                }
            }
        }

        private static bool IsEligible(in PartyMemberStatusEntry entry)
        {
            return entry.CharacterID != CharacterID.Invalid
                && entry.EntityID != EntityID.Invalid
                && (entry.Status == PartyMemberStatus.Online || entry.Status == PartyMemberStatus.Dead);
        }

        // A pair that returns early (no client, a throwing read) is not stamped, so the sweep forgets it.
        private void ProcessPair(in PartyMemberStatusEntry recipient, in PartyMemberStatusEntry member, uint tick)
        {
            uint clientId;
            try
            {
                if (!_clients.TryGetClient(recipient.CharacterID, out clientId))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LogErrorOnce(recipient.EntityID, ex);
                return;
            }

            if (!TryReadStats(member, out int hp, out int maxHp, out int mp, out int maxMp))
            {
                return;
            }

            ulong key = ((ulong)recipient.CharacterID.RawValue << KEY_SHIFT) | member.CharacterID.RawValue;
            if (Track(key, tick, hp, maxHp, mp, maxMp))
            {
                Send(clientId, member.EntityID, hp, maxHp, mp, maxMp);
            }
        }

        // Absolute wire values: floored, never below 0 (a NaN or negative stat reads as 0); a Dead member has HP 0.
        private bool TryReadStats(in PartyMemberStatusEntry member, out int hp, out int maxHp, out int mp, out int maxMp)
        {
            try
            {
                hp = member.Status == PartyMemberStatus.Dead ? 0 : ToWire(_stats.GetCurrentHP(member.EntityID));
                maxHp = Math.Max(0, _stats.GetEffectiveStat(member.EntityID, StatID.MaxHP));
                mp = ToWire(_stats.GetCurrentMP(member.EntityID));
                maxMp = Math.Max(0, _stats.GetEffectiveStat(member.EntityID, StatID.MaxMP));
                return true;
            }
            catch (Exception ex)
            {
                LogErrorOnce(member.EntityID, ex);
                hp = maxHp = mp = maxMp = 0;
                return false;
            }
        }

        private static int ToWire(float value)
        {
            return float.IsNaN(value) || value <= 0f ? 0 : Mathf.FloorToInt(value);
        }

        // Stamps the pair, decides whether to send, and records what is sent. A new pair always sends; it is
        // remembered only while the storage has room.
        private bool Track(ulong key, uint tick, int hp, int maxHp, int mp, int maxMp)
        {
            if (_index.TryGetValue(key, out int slot))
            {
                _stamp[slot] = tick;
                if (!ShouldSend(slot, hp, maxHp, mp, maxMp))
                {
                    return false;
                }

                Store(slot, hp, maxHp, mp, maxMp);
                return true;
            }

            if (_count >= _capacity)
            {
                WarnFullOnce();
                return true;
            }

            slot = _count++;
            _keys[slot] = key;
            _stamp[slot] = tick;
            Store(slot, hp, maxHp, mp, maxMp);
            _index.Add(key, slot);
            return true;
        }

        private bool ShouldSend(int slot, int hp, int maxHp, int mp, int maxMp)
        {
            int lastHp = _lastHp[slot];
            int lastMp = _lastMp[slot];
            if (hp == lastHp && maxHp == _lastMaxHp[slot] && mp == lastMp && maxMp == _lastMaxMp[slot])
            {
                return false;
            }

            if (maxHp != _lastMaxHp[slot] || maxMp != _lastMaxMp[slot])
            {
                return true;
            }

            if (Crosses(hp - lastHp, maxHp) || Crosses(mp - lastMp, maxMp))
            {
                return true;
            }

            bool full = hp >= maxHp && mp >= maxMp;
            bool wasFull = lastHp >= _lastMaxHp[slot] && lastMp >= _lastMaxMp[slot];
            if (full && !wasFull)
            {
                return true;
            }

            return (hp == 0) != (lastHp == 0);
        }

        private bool Crosses(int delta, int max)
        {
            if (delta == 0)
            {
                return false;
            }

            if (max <= 0)
            {
                return true;
            }

            // The explicit cast forces float rounding of the product (no wider intermediate precision).
            float limit = (float)(_threshold * (float)max);
            return (float)Math.Abs(delta) >= limit;
        }

        private void Store(int slot, int hp, int maxHp, int mp, int maxMp)
        {
            _lastHp[slot] = hp;
            _lastMaxHp[slot] = maxHp;
            _lastMp[slot] = mp;
            _lastMaxMp[slot] = maxMp;
        }

        private void Send(uint clientId, EntityID entity, int hp, int maxHp, int mp, int maxMp)
        {
            var update = new PartyMemberHealthUpdate(entity, hp, maxHp, mp, maxMp);
            Span<byte> body = stackalloc byte[PartyMemberHealthUpdate.BodySize];
            BatchSubMessageCodec.WritePartyMemberHealthUpdateBody(body, in update);
            _collector.Enqueue(clientId, RUBatchCategory.PartyMemberHealthUpdate, PartyMemberHealthUpdate.MessageTypeId,
                body);
        }

        /// <summary>Drops every record whose stamp is not <paramref name="tick"/> (swap-remove, no allocation).</summary>
        private void Sweep(uint tick)
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_stamp[i] == tick)
                {
                    continue;
                }

                _index.Remove(_keys[i]);
                int last = _count - 1;
                if (i != last)
                {
                    _keys[i] = _keys[last];
                    _stamp[i] = _stamp[last];
                    Store(i, _lastHp[last], _lastMaxHp[last], _lastMp[last], _lastMaxMp[last]);
                    _index[_keys[i]] = i;
                }

                _count = last;
            }
        }

        private void LogErrorOnce(EntityID entity, Exception ex)
        {
            if (_loggedError)
            {
                return;
            }

            _loggedError = true;
            Debug.LogError(LOG_PREFIX + "stat or client read threw for " + entity + ": " + ex.GetType().Name + ": " + ex.Message
                + " (logged once per run).");
        }

        private void WarnFullOnce()
        {
            if (_warnedFull)
            {
                return;
            }

            _warnedFull = true;
            Debug.LogWarning(LOG_PREFIX + "record storage full (" + _capacity
                + " pairs); extra pairs are sent every eligible tick but not remembered.");
        }
    }
}
