using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// Tick-thread table of gateway admissions awaiting their <c>SessionHandshake</c> (Authentication Story 013,
    /// CR-AUTH-11 steps 4 and 6, ADR-016 Decision 4). One entry per account: <c>(CharacterID, admittedAtTick)</c> plus a
    /// reservation flag. A RESERVED entry counts toward the zone's session count; an entry that lost its reservation at
    /// T-4 (leaving Active) is KEPT so a late handshake can still be answered, until its own timeout drops it.
    /// </summary>
    /// <remarks>
    /// Not thread-safe: every method runs on the tick thread. <see cref="OnTick"/> allocates nothing in steady state.
    /// An entry whose handshake was accepted and whose <c>LoadCharacter</c> is still in flight does not time out
    /// (ADR-016: the drop is for "no <c>SessionHandshake</c> in time"); if that load ends without a slot, the entry
    /// times out on the next tick past its deadline. A new admission for a character that another account's entry
    /// holds releases that older entry (token invalidated, <c>SessionReleased</c> queued, warning logged) — a character
    /// belongs to one account, so this signals a gateway bug.
    /// </remarks>
    public sealed class AdmittedEntries : IZoneEntryReservationSink
    {
        /// <summary>Default <c>CONNECTING_TIMEOUT_SECONDS</c> (GDD default, <c>networking-session.md</c>); ticks = seconds x TICK_RATE_HZ.</summary>
        public const int CONNECTING_TIMEOUT_SECONDS = 30;

        private const string LOG_PREFIX = "[AdmittedEntries]";

        private sealed class Entry
        {
            public AccountID Account;
            public CharacterID Character;
            public uint AdmittedAtTick;
            public bool Reserved;
        }

        private sealed class CharacterIdComparer : IEqualityComparer<CharacterID>
        {
            public static readonly CharacterIdComparer Instance = new CharacterIdComparer();

            public bool Equals(CharacterID x, CharacterID y) => x.RawValue == y.RawValue;

            public int GetHashCode(CharacterID obj) => obj.RawValue.GetHashCode();
        }

        private readonly SessionTokenStore _tokens;
        private readonly IGatewayLinkOutbox _outbox;
        private readonly Action<uint> _onReservedCountChanged;
        private readonly Func<CharacterID, bool> _isLoadPending;
        private readonly long _timeoutTicks;
        private readonly Dictionary<AccountID, Entry> _byAccount = new Dictionary<AccountID, Entry>(AccountID.AccountIDComparer.Instance);
        private readonly Dictionary<CharacterID, Entry> _byCharacter = new Dictionary<CharacterID, Entry>(CharacterIdComparer.Instance);
        private readonly List<Entry> _scratch = new List<Entry>();
        private int _reserved;

        /// <summary>Creates the table.</summary>
        /// <param name="tokens">The token store; a timed-out entry's token is invalidated here.</param>
        /// <param name="outbox">Receives <c>SessionReleased</c>.</param>
        /// <param name="connectingTimeoutTicks">CONNECTING_TIMEOUT in ticks; must be positive.</param>
        /// <param name="onReservedCountChanged">Optional; called with the tick whenever <see cref="ReservedCount"/> changes.</param>
        /// <param name="isLoadPending">Optional; true while the character's <c>LoadCharacter</c> is in flight
        /// (<see cref="ZoneEntryAdmission.IsLoadPending"/>). Such an entry is not timed out. Null: no entry is pending.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tokens"/> or <paramref name="outbox"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="connectingTimeoutTicks"/> is not positive.</exception>
        public AdmittedEntries(SessionTokenStore tokens, IGatewayLinkOutbox outbox, int connectingTimeoutTicks,
            Action<uint> onReservedCountChanged = null, Func<CharacterID, bool> isLoadPending = null)
        {
            _isLoadPending = isLoadPending;
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            if (connectingTimeoutTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectingTimeoutTicks),
                    $"connectingTimeoutTicks must be positive (was {connectingTimeoutTicks}).");
            }

            _timeoutTicks = connectingTimeoutTicks;
            _onReservedCountChanged = onReservedCountChanged;
        }

        /// <summary>CONNECTING_TIMEOUT in ticks.</summary>
        public int ConnectingTimeoutTicks => (int)_timeoutTicks;

        /// <summary>Number of entries still holding a reservation.</summary>
        public int ReservedCount => _reserved;

        /// <summary>Number of entries, reserved or kept.</summary>
        public int EntryCount => _byAccount.Count;

        /// <summary>True when <paramref name="account"/> has an entry that holds a reservation.</summary>
        /// <param name="account">The account to look up.</param>
        public bool IsReserved(AccountID account) => _byAccount.TryGetValue(account, out Entry e) && e.Reserved;

        /// <summary>Records an admission, or replaces the account's existing entry (new character and tick, reserved).</summary>
        /// <param name="account">The admitted account.</param>
        /// <param name="character">The character the account will enter with.</param>
        /// <param name="serverTick">The admit tick; the timeout counts from here.</param>
        public void Admit(AccountID account, CharacterID character, uint serverTick)
        {
            int before = _reserved;

            // Another account's entry for the same character is released: a character belongs to one account.
            if (_byCharacter.TryGetValue(character, out Entry holder) && holder.Account != account)
            {
                bool wasReserved = holder.Reserved;
                Remove(holder);
                _tokens.InvalidateToken(holder.Account.RawValue);
                if (wasReserved)
                {
                    _outbox.EnqueueSessionReleased(holder.Account);
                }

                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} character {character} admitted for {account} while {holder.Account} held an entry for it; the older entry is released.");
            }

            if (_byAccount.TryGetValue(account, out Entry existing))
            {
                RemoveCharacterKey(existing);
                existing.Character = character;
                existing.AdmittedAtTick = serverTick;
                if (!existing.Reserved)
                {
                    existing.Reserved = true;
                    _reserved++;
                }

                _byCharacter[character] = existing;
            }
            else
            {
                var entry = new Entry { Account = account, Character = character, AdmittedAtTick = serverTick, Reserved = true };
                _byAccount[account] = entry;
                _byCharacter[character] = entry;
                _reserved++;
            }

            NotifyIfChanged(before, serverTick);
        }

        /// <summary>
        /// Appends the account of every entry that still holds a reservation to <paramref name="target"/> (Gateway
        /// Story 011: the admitted-but-not-arrived accounts of the <c>RegisterZone</c> snapshot). Tick thread; no allocation.
        /// </summary>
        /// <param name="target">The list to append to; not null.</param>
        public void CopyReservedAccounts(List<AccountID> target)
        {
            foreach (KeyValuePair<AccountID, Entry> pair in _byAccount)
            {
                if (pair.Value.Reserved)
                {
                    target.Add(pair.Key);
                }
            }
        }

        /// <summary>Looks up the entry for <paramref name="character"/>, reserved or kept. No allocation.</summary>
        /// <param name="character">The character presented in the handshake.</param>
        /// <param name="account">The admitted account when found; <see cref="AccountID.Invalid"/> otherwise.</param>
        /// <param name="reserved">True when the entry still holds its reservation.</param>
        /// <returns>True when an entry exists.</returns>
        public bool TryGetByCharacter(CharacterID character, out AccountID account, out bool reserved)
        {
            if (_byCharacter.TryGetValue(character, out Entry e))
            {
                account = e.Account;
                reserved = e.Reserved;
                return true;
            }

            account = AccountID.Invalid;
            reserved = false;
            return false;
        }

        /// <summary>
        /// Gateway <c>Evict</c> (Authentication Story 014): deletes the account's entry, reserved or kept. The token is NOT
        /// touched and no <c>SessionReleased</c> is queued (the gateway removes its own map entry on <c>EvictResult</c>).
        /// </summary>
        /// <param name="account">The evicted account.</param>
        /// <param name="serverTick">The current tick, passed to the reserved-count notification.</param>
        /// <param name="character">The entry's character when one existed; otherwise default.</param>
        /// <returns>True when an entry existed and was removed.</returns>
        public bool TryEvict(AccountID account, uint serverTick, out CharacterID character)
        {
            if (!_byAccount.TryGetValue(account, out Entry e))
            {
                character = default;
                return false;
            }

            character = e.Character;
            int before = _reserved;
            Remove(e);
            NotifyIfChanged(before, serverTick);
            return true;
        }

        /// <summary>Step 4: the reservation ends. The entry is removed (the token stays in the store); no <c>SessionReleased</c>.</summary>
        /// <param name="character">The character that now holds a slot.</param>
        /// <param name="serverTick">The allocation tick.</param>
        public void OnSlotAllocated(CharacterID character, uint serverTick)
        {
            if (!_byCharacter.TryGetValue(character, out Entry e) || !e.Reserved)
            {
                return;
            }

            int before = _reserved;
            Remove(e);
            NotifyIfChanged(before, serverTick);
        }

        /// <summary>T-4 (AC-ZI-35): every reserved entry loses its reservation, is kept, and gets one <c>SessionReleased</c>.</summary>
        /// <param name="serverTick">The tick of the transition.</param>
        public void OnLeftActive(uint serverTick)
        {
            int before = _reserved;
            foreach (KeyValuePair<AccountID, Entry> pair in _byAccount)
            {
                Entry e = pair.Value;
                if (!e.Reserved)
                {
                    continue;
                }

                e.Reserved = false;
                _reserved--;
                _outbox.EnqueueSessionReleased(e.Account);
            }

            NotifyIfChanged(before, serverTick);
        }

        /// <summary>
        /// Drops entries whose timeout has passed: a reserved entry invalidates its token and queues <c>SessionReleased</c>;
        /// a kept entry only invalidates its token (its release was queued at T-4).
        /// </summary>
        /// <param name="serverTick">The current tick.</param>
        public void OnTick(uint serverTick)
        {
            if (_byAccount.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<AccountID, Entry> pair in _byAccount)
            {
                if (serverTick >= pair.Value.AdmittedAtTick + _timeoutTicks
                    && (_isLoadPending == null || !_isLoadPending(pair.Value.Character)))
                {
                    _scratch.Add(pair.Value);
                }
            }

            if (_scratch.Count == 0)
            {
                return;
            }

            int before = _reserved;
            for (int i = 0; i < _scratch.Count; i++)
            {
                Entry e = _scratch[i];
                bool wasReserved = e.Reserved;
                Remove(e);
                _tokens.InvalidateToken(e.Account.RawValue);
                if (wasReserved)
                {
                    _outbox.EnqueueSessionReleased(e.Account);
                }
            }

            _scratch.Clear();
            NotifyIfChanged(before, serverTick);
        }

        private void Remove(Entry e)
        {
            _byAccount.Remove(e.Account);
            RemoveCharacterKey(e);
            if (e.Reserved)
            {
                e.Reserved = false;
                _reserved--;
            }
        }

        private void RemoveCharacterKey(Entry e)
        {
            if (_byCharacter.TryGetValue(e.Character, out Entry current) && ReferenceEquals(current, e))
            {
                _byCharacter.Remove(e.Character);
            }
        }

        private void NotifyIfChanged(int before, uint serverTick)
        {
            if (_reserved != before)
            {
                _onReservedCountChanged?.Invoke(serverTick);
            }
        }
    }
}
