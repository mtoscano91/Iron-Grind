using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// The player and mob slot pools of one zone instance plus the shared EntityID counter
    /// (<c>zone-instancing.md</c> CR-ZI-3, CR-ZI-4, CR-ZI-5, CR-ZI-15, EC-ZI-4, EC-ZI-12).
    /// One zone per process (ADR-007, ADR-009), so methods taking a <see cref="ZoneID"/> reject any
    /// id other than the one given at construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Slots.</b> Player slots are <c>byte</c> indices, mob slots <c>ushort</c> indices. Each pool is
    /// a LIFO free-list over preallocated arrays: allocation and release are O(1) and allocate nothing
    /// after construction. An index is never reassigned while occupied; the most recently freed index
    /// is the next one handed out. A fresh pool hands out 0, 1, 2, ... The player roster is also
    /// updated in O(1) (swap-remove), so its order is unspecified.
    /// </para>
    /// <para>
    /// <b>EntityIDs.</b> One monotonic counter shared by players and mobs starts at the constructor's
    /// <c>firstEntityId</c> (default 1); 0 is never issued. After <see cref="uint.MaxValue"/> has been
    /// issued, the first allocation attempt that reaches ID minting fails without taking a slot, logs
    /// one <c>EntityIDCounterExhausted</c> critical alert and calls
    /// <see cref="IZoneSlotPoolListener.OnEntityIDCounterExhausted"/> once; later failures are silent.
    /// An attempt that fails earlier (foreign zone, full pool) raises nothing.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Main thread only: the capacity check and the slot write are atomic because
    /// the caller serialises every call. No locks or interlocked operations are used. The listener is
    /// called directly (ADR-010) after the pool state is final and must not throw.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pool = new ZoneSlotPool(new ZoneID(7u), tickSource, listener);
    /// if (pool.TryAllocatePlayer(zone, characterId, accountId, clientId, out byte slot, out EntityID id))
    /// {
    ///     pool.SetSessionState(id, ZoneSessionState.Dead);
    ///     IReadOnlyList&lt;EntityID&gt; roster = pool.GetPlayerEntityIDsInZone(zone);
    ///     pool.ReleasePlayer(id);   // listener.OnOccupiedPlayerSlotsChanged(0, tick)
    /// }
    /// </code>
    /// </example>
    public sealed class ZoneSlotPool
    {
        /// <summary>Default <c>MAX_PLAYERS_PER_ZONE</c> (GDD tuning knob).</summary>
        public const int MAX_PLAYERS_PER_ZONE = 50;

        /// <summary>Default <c>MAX_MOBS_PER_ZONE</c> (GDD tuning knob).</summary>
        public const int MAX_MOBS_PER_ZONE = 150;

        private const int MAX_PLAYER_SLOT_CEILING = byte.MaxValue;
        private const int MAX_MOB_SLOT_CEILING = ushort.MaxValue;
        private const string LOG_PREFIX = "[ZoneSlotPool]";

        private static readonly ReadOnlyCollection<EntityID> EMPTY_ROSTER =
            new ReadOnlyCollection<EntityID>(new List<EntityID>(0));

        private readonly ZoneID _zone;
        private readonly IServerTickSource _ticks;
        private readonly IZoneSlotPoolListener _listener;
        private IZoneSlotReleaseListener _releaseListener;
        private readonly int _maxPlayers;
        private readonly int _maxMobs;

        private readonly EntityID[] _playerEntity;
        private readonly CharacterID[] _playerCharacter;
        private readonly AccountID[] _playerAccount;
        private readonly uint[] _playerClient;
        private readonly ZoneSessionState[] _playerState;
        private readonly int[] _playerFree;
        private readonly Dictionary<EntityID, int> _playerIndex;
        private int _playerFreeCount;

        private readonly EntityID[] _mobEntity;
        private readonly int[] _mobFree;
        private readonly Dictionary<EntityID, int> _mobIndex;
        private int _mobFreeCount;

        private readonly List<EntityID> _rosterList;
        private readonly int[] _rosterPos;
        private readonly ReadOnlyCollection<EntityID> _roster;

        private readonly List<EntityID> _mobRosterList;
        private readonly int[] _mobRosterPos;
        private readonly ReadOnlyCollection<EntityID> _mobRoster;

        private uint _nextEntityId;
        private bool _counterSpent;
        private bool _exhaustionRaised;
        private bool _foreignRosterWarned;
        private bool _foreignMobRosterWarned;

        /// <summary>Creates empty pools for <paramref name="zone"/>.</summary>
        /// <param name="zone">The zone this process hosts. Must not be <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="ticks">Source of the server tick passed to listener calls. Must not be null.</param>
        /// <param name="listener">Receiver of pool events. Must not be null.</param>
        /// <param name="maxPlayers"><c>MAX_PLAYERS_PER_ZONE</c>, in [1, 255] (byte index ceiling).</param>
        /// <param name="maxMobs"><c>MAX_MOBS_PER_ZONE</c>, in [1, 65535] (ushort index ceiling).</param>
        /// <param name="firstEntityId">First EntityID issued; must not be 0. Tests pass a high value to reach EC-ZI-12.</param>
        /// <exception cref="ArgumentNullException"><paramref name="ticks"/> or <paramref name="listener"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is the invalid (0) zone id.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A capacity is out of range or <paramref name="firstEntityId"/> is 0.</exception>
        public ZoneSlotPool(
            ZoneID zone,
            IServerTickSource ticks,
            IZoneSlotPoolListener listener,
            int maxPlayers = MAX_PLAYERS_PER_ZONE,
            int maxMobs = MAX_MOBS_PER_ZONE,
            uint firstEntityId = 1u)
        {
            ValidateArguments(zone, ticks, listener, maxPlayers, maxMobs, firstEntityId);

            _zone = zone;
            _ticks = ticks;
            _listener = listener;
            _maxPlayers = maxPlayers;
            _maxMobs = maxMobs;
            _nextEntityId = firstEntityId;

            _playerEntity = new EntityID[maxPlayers];
            _playerCharacter = new CharacterID[maxPlayers];
            _playerAccount = new AccountID[maxPlayers];
            _playerClient = new uint[maxPlayers];
            _playerState = new ZoneSessionState[maxPlayers];
            _playerFree = CreateFreeList(maxPlayers);
            _playerIndex = new Dictionary<EntityID, int>(maxPlayers);
            _playerFreeCount = maxPlayers;

            _mobEntity = new EntityID[maxMobs];
            _mobFree = CreateFreeList(maxMobs);
            _mobIndex = new Dictionary<EntityID, int>(maxMobs);
            _mobFreeCount = maxMobs;

            _rosterList = new List<EntityID>(maxPlayers);
            _rosterPos = new int[maxPlayers];
            _roster = new ReadOnlyCollection<EntityID>(_rosterList);

            _mobRosterList = new List<EntityID>(maxMobs);
            _mobRosterPos = new int[maxMobs];
            _mobRoster = new ReadOnlyCollection<EntityID>(_mobRosterList);
        }

        /// <summary>The zone this pool serves.</summary>
        public ZoneID Zone => _zone;

        /// <summary>Number of player slots in the pool (the capacity).</summary>
        public int MaxPlayers => _maxPlayers;

        /// <summary>Number of mob slots in the pool.</summary>
        public int MaxMobs => _maxMobs;

        /// <summary>Player slots currently held, in any <see cref="ZoneSessionState"/>.</summary>
        public int OccupiedPlayerCount => _maxPlayers - _playerFreeCount;

        /// <summary>Mob slots currently held.</summary>
        public int OccupiedMobCount => _maxMobs - _mobFreeCount;

        /// <summary>
        /// Allocates a player slot in state <see cref="ZoneSessionState.Connected"/> (CR-ZI-8 step 5).
        /// The capacity check and the write are this one call. Fails, without side effects, for a
        /// foreign zone (one warning), a full pool or an exhausted EntityID counter (see class remarks).
        /// On success the listener gets <see cref="IZoneSlotPoolListener.OnOccupiedPlayerSlotsChanged"/>.
        /// </summary>
        /// <param name="zone">Must equal <see cref="Zone"/>.</param>
        /// <param name="character">The character entering.</param>
        /// <param name="account">The owning account.</param>
        /// <param name="clientId">The connection id.</param>
        /// <param name="slot">The allocated index, or 0 on failure.</param>
        /// <param name="id">The minted EntityID, or <see cref="EntityID.Invalid"/> on failure.</param>
        /// <returns>True when a slot was allocated.</returns>
        public bool TryAllocatePlayer(
            ZoneID zone,
            CharacterID character,
            AccountID account,
            uint clientId,
            out byte slot,
            out EntityID id)
        {
            slot = 0;
            id = EntityID.Invalid;

            if (!IsOwnZone(zone, nameof(TryAllocatePlayer)) || _playerFreeCount == 0)
            {
                return false;
            }

            if (!TryMintId(out EntityID minted))
            {
                return false;
            }

            int index = _playerFree[--_playerFreeCount];
            _playerEntity[index] = minted;
            _playerCharacter[index] = character;
            _playerAccount[index] = account;
            _playerClient[index] = clientId;
            _playerState[index] = ZoneSessionState.Connected;
            _playerIndex.Add(minted, index);
            _rosterPos[index] = _rosterList.Count;
            _rosterList.Add(minted);

            slot = (byte)index;
            id = minted;
            _listener.OnOccupiedPlayerSlotsChanged(OccupiedPlayerCount, _ticks.ServerTickNumber);
            return true;
        }

        /// <summary>
        /// Registers the before-release listener (CR-ZI-4; Status Effects CR-SE-11). Set once, because the
        /// listener usually needs a slot index built over this pool, so it cannot be a constructor argument.
        /// Without a listener the pool behaves as before.
        /// </summary>
        /// <param name="listener">Receiver of <see cref="IZoneSlotReleaseListener.BeforeRelease"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="listener"/> is null.</exception>
        /// <exception cref="InvalidOperationException">A listener is already registered.</exception>
        public void SetReleaseListener(IZoneSlotReleaseListener listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (_releaseListener != null)
            {
                throw new InvalidOperationException($"{LOG_PREFIX} a release listener is already registered.");
            }

            _releaseListener = listener;
        }

        /// <summary>
        /// Calls the release listener, if any, before the slot changes. An exception is caught and logged; the
        /// release then completes anyway.
        /// </summary>
        private void NotifyBeforeRelease(EntityID id)
        {
            if (_releaseListener == null)
            {
                return;
            }

            try
            {
                _releaseListener.BeforeRelease(id);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} release listener threw for {id}; releasing the slot anyway: {ex}");
            }
        }

        /// <summary>
        /// Zeroes the player's slot and returns it to the free-list. An unknown or already-released
        /// EntityID returns false and changes nothing and calls no listener. Otherwise the release listener
        /// (see <see cref="SetReleaseListener"/>) is called first, while the slot is still held, then the slot
        /// is zeroed and the pool listener is notified.
        /// </summary>
        /// <param name="id">The EntityID issued by <see cref="TryAllocatePlayer"/>.</param>
        /// <returns>True when a slot was released.</returns>
        public bool ReleasePlayer(EntityID id)
        {
            if (!_playerIndex.TryGetValue(id, out int index))
            {
                return false;
            }

            NotifyBeforeRelease(id);
            _playerIndex.Remove(id);
            _playerEntity[index] = EntityID.Invalid;
            _playerCharacter[index] = CharacterID.Invalid;
            _playerAccount[index] = AccountID.Invalid;
            _playerClient[index] = 0u;
            _playerState[index] = ZoneSessionState.Connected;
            _playerFree[_playerFreeCount++] = index;

            int pos = _rosterPos[index];
            int last = _rosterList.Count - 1;
            if (pos != last)
            {
                EntityID moved = _rosterList[last];
                _rosterList[pos] = moved;
                _rosterPos[_playerIndex[moved]] = pos;
            }

            _rosterList.RemoveAt(last);

            _listener.OnOccupiedPlayerSlotsChanged(OccupiedPlayerCount, _ticks.ServerTickNumber);
            return true;
        }

        /// <summary>Looks up the slot index of an occupied player.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="slot">The slot index, or 0 when unknown.</param>
        /// <returns>True when <paramref name="id"/> holds a player slot.</returns>
        public bool TryGetPlayerSlot(EntityID id, out byte slot)
        {
            if (_playerIndex.TryGetValue(id, out int index))
            {
                slot = (byte)index;
                return true;
            }

            slot = 0;
            return false;
        }

        /// <summary>
        /// Looks up the slot index of an occupied mob (Status Effects Story 009). Allocation-free.
        /// </summary>
        /// <param name="id">The mob's EntityID.</param>
        /// <param name="mobSlot">The mob slot index, or 0 when unknown or released.</param>
        /// <returns>True when <paramref name="id"/> holds a mob slot.</returns>
        /// <example>
        /// <code>
        /// if (pool.TryGetMobSlot(id, out ushort mobSlot)) { int owl = pool.GetOwlIndexForMobSlot(mobSlot); }
        /// </code>
        /// </example>
        public bool TryGetMobSlot(EntityID id, out ushort mobSlot)
        {
            if (_mobIndex.TryGetValue(id, out int index))
            {
                mobSlot = (ushort)index;
                return true;
            }

            mobSlot = 0;
            return false;
        }

        /// <summary>
        /// Reads the EntityID and connection id held by one player slot index (Movement Story 009). Allocation-free.
        /// </summary>
        /// <param name="slot">A slot index in [0, <see cref="MaxPlayers"/>).</param>
        /// <param name="id">The occupant's EntityID, or <see cref="EntityID.Invalid"/> when empty or out of range.</param>
        /// <param name="clientId">The occupant's connection id, or 0.</param>
        /// <returns>True when the slot is occupied.</returns>
        public bool TryGetPlayerAtSlot(int slot, out EntityID id, out uint clientId)
        {
            if (slot >= 0 && slot < _maxPlayers && _playerEntity[slot] != EntityID.Invalid)
            {
                id = _playerEntity[slot];
                clientId = _playerClient[slot];
                return true;
            }

            id = EntityID.Invalid;
            clientId = 0u;
            return false;
        }

        /// <summary>Reads a copy of an occupied player slot.</summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="player">The slot contents, or default when unknown.</param>
        /// <returns>True when <paramref name="id"/> holds a player slot.</returns>
        public bool TryGetPlayer(EntityID id, out ZonePlayerSlot player)
        {
            if (_playerIndex.TryGetValue(id, out int index))
            {
                player = new ZonePlayerSlot(_playerEntity[index], _playerCharacter[index],
                    _playerAccount[index], _playerClient[index], _playerState[index]);
                return true;
            }

            player = default;
            return false;
        }

        /// <summary>
        /// Sets the session state of an occupied player. Never changes occupancy and never notifies
        /// the listener.
        /// </summary>
        /// <param name="id">The player's EntityID.</param>
        /// <param name="state">The new state.</param>
        /// <returns>False for an unknown EntityID.</returns>
        public bool SetSessionState(EntityID id, ZoneSessionState state)
        {
            if (!_playerIndex.TryGetValue(id, out int index))
            {
                return false;
            }

            _playerState[index] = state;
            return true;
        }

        /// <summary>
        /// Allocates a mob slot and mints an EntityID (the base of Story 003's <c>TryAddMob</c>).
        /// Does not notify the listener. Fails for a foreign zone (one warning), a full pool or an
        /// exhausted counter.
        /// </summary>
        /// <param name="zone">Must equal <see cref="Zone"/>.</param>
        /// <param name="mobSlot">The allocated index, or 0 on failure.</param>
        /// <param name="id">The minted EntityID, or <see cref="EntityID.Invalid"/> on failure.</param>
        /// <returns>True when a slot was allocated.</returns>
        public bool TryAllocateMob(ZoneID zone, out ushort mobSlot, out EntityID id)
        {
            mobSlot = 0;
            id = EntityID.Invalid;

            if (!IsOwnZone(zone, nameof(TryAllocateMob)) || _mobFreeCount == 0)
            {
                return false;
            }

            if (!TryMintId(out EntityID minted))
            {
                return false;
            }

            int index = _mobFree[--_mobFreeCount];
            _mobEntity[index] = minted;
            _mobIndex.Add(minted, index);
            _mobRosterPos[index] = _mobRosterList.Count;
            _mobRosterList.Add(minted);

            mobSlot = (ushort)index;
            id = minted;
            return true;
        }

        /// <summary>
        /// Releases a mob slot. Unknown or already-released ids return false and call no listener. Otherwise the
        /// release listener is called before the slot changes, as in <see cref="ReleasePlayer"/>.
        /// </summary>
        /// <param name="id">The mob's EntityID.</param>
        /// <returns>True when a slot was released.</returns>
        public bool ReleaseMob(EntityID id)
        {
            if (!_mobIndex.TryGetValue(id, out int index))
            {
                return false;
            }

            NotifyBeforeRelease(id);
            _mobIndex.Remove(id);
            _mobEntity[index] = EntityID.Invalid;
            _mobFree[_mobFreeCount++] = index;

            int pos = _mobRosterPos[index];
            int last = _mobRosterList.Count - 1;
            if (pos != last)
            {
                EntityID moved = _mobRosterList[last];
                _mobRosterList[pos] = moved;
                _mobRosterPos[_mobIndex[moved]] = pos;
            }

            _mobRosterList.RemoveAt(last);
            return true;
        }

        /// <summary>
        /// The index of a mob slot in the OWL compensation array (CR-ZI-4):
        /// <c>maxPlayers + mobSlot</c>, i.e. [50, 199] with the default sizes.
        /// </summary>
        /// <param name="mobSlot">A mob slot index below <see cref="MaxMobs"/>.</param>
        /// <returns>The OWL array index.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="mobSlot"/> is not below <see cref="MaxMobs"/>.</exception>
        public int GetOwlIndexForMobSlot(ushort mobSlot)
        {
            if (mobSlot >= _maxMobs)
            {
                throw new ArgumentOutOfRangeException(nameof(mobSlot), mobSlot,
                    $"{LOG_PREFIX} mob slot must be below {_maxMobs}.");
            }

            return _maxPlayers + mobSlot;
        }

        /// <summary>
        /// The EntityIDs of all occupied player slots in unspecified order (CR-ZI-15): no empty slots,
        /// no mobs. The same read-only instance is returned on every call and is refreshed on each
        /// player allocation or release (O(1)), so reading it every tick allocates nothing. A foreign
        /// zone gets a shared empty read-only list; the first such call per pool logs one warning, later
        /// ones are silent (this is polled every tick).
        /// </summary>
        /// <param name="zone">Must equal <see cref="Zone"/>.</param>
        /// <returns>A read-only view; casting to a mutable list type fails or throws on mutation.</returns>
        public IReadOnlyList<EntityID> GetPlayerEntityIDsInZone(ZoneID zone)
        {
            if (zone == _zone)
            {
                return _roster;
            }

            if (!_foreignRosterWarned)
            {
                _foreignRosterWarned = true;
                IsOwnZone(zone, nameof(GetPlayerEntityIDsInZone));
            }

            return EMPTY_ROSTER;
        }

        /// <summary>
        /// The EntityIDs of all occupied mob slots in unspecified order (swap-remove on release), built like
        /// <see cref="GetPlayerEntityIDsInZone"/>: the same read-only instance on every call, refreshed in O(1) on
        /// each mob allocation or release, so reading it allocates nothing. A foreign zone gets a shared empty
        /// read-only list; the first such call per pool logs one warning, later ones are silent.
        /// </summary>
        /// <param name="zone">Must equal <see cref="Zone"/>.</param>
        /// <returns>A read-only view.</returns>
        public IReadOnlyList<EntityID> GetMobEntityIDsInZone(ZoneID zone)
        {
            if (zone == _zone)
            {
                return _mobRoster;
            }

            if (!_foreignMobRosterWarned)
            {
                _foreignMobRosterWarned = true;
                IsOwnZone(zone, nameof(GetMobEntityIDsInZone));
            }

            return EMPTY_ROSTER;
        }

        private static void ValidateArguments(ZoneID zone, IServerTickSource ticks, IZoneSlotPoolListener listener,
            int maxPlayers, int maxMobs, uint firstEntityId)
        {
            if (ticks == null)
            {
                throw new ArgumentNullException(nameof(ticks));
            }

            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            if (maxPlayers < 1 || maxPlayers > MAX_PLAYER_SLOT_CEILING)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), maxPlayers,
                    $"{LOG_PREFIX} maxPlayers must be in [1, {MAX_PLAYER_SLOT_CEILING}].");
            }

            if (maxMobs < 1 || maxMobs > MAX_MOB_SLOT_CEILING)
            {
                throw new ArgumentOutOfRangeException(nameof(maxMobs), maxMobs,
                    $"{LOG_PREFIX} maxMobs must be in [1, {MAX_MOB_SLOT_CEILING}].");
            }

            if (firstEntityId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(firstEntityId), firstEntityId,
                    $"{LOG_PREFIX} firstEntityId must not be 0 (EntityID.Invalid).");
            }
        }

        private static int[] CreateFreeList(int size)
        {
            var free = new int[size];
            for (int i = 0; i < size; i++)
            {
                free[i] = size - 1 - i; // top of the stack is index 0
            }

            return free;
        }

        private bool IsOwnZone(ZoneID zone, string operation)
        {
            if (zone == _zone)
            {
                return true;
            }

            Debug.LogWarning($"{LOG_PREFIX} {operation} rejected: foreign zone {zone} (this pool serves {_zone}).");
            return false;
        }

        private bool TryMintId(out EntityID id)
        {
            if (_counterSpent)
            {
                id = EntityID.Invalid;
                if (!_exhaustionRaised)
                {
                    _exhaustionRaised = true;
                    uint tick = _ticks.ServerTickNumber;
                    Debug.LogError($"{LOG_PREFIX} EntityIDCounterExhausted: no EntityID left in {_zone} (tick {tick}); the zone must close (EC-ZI-12).");
                    _listener.OnEntityIDCounterExhausted(tick);
                }

                return false;
            }

            uint raw = _nextEntityId;
            if (raw == uint.MaxValue)
            {
                _counterSpent = true; // never wrap to 0
            }
            else
            {
                _nextEntityId = raw + 1u;
            }

            id = new EntityID(raw);
            return true;
        }
    }
}
