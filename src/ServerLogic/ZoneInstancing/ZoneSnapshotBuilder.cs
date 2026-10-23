using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.LootTableSystem;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Builds the zone state snapshot for one joining client and sends it as R-OD
    /// <see cref="ZoneStateSnapshotFragment"/> messages (<c>zone-instancing.md</c> CR-ZI-8 step 6, F-ZI-1, AC-ZI-8).
    /// Zone Instancing Story 007.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Serialize first.</b> Every entity becomes one <see cref="EntityState"/> entry, players (roster order) then
    /// mobs (roster order), written back to back into one per-zone buffer sized
    /// <c>maxPlayers x 77 + maxMobs x 35</c>. Only then is <c>totalFragments = max(1, ceil(bytes / 504))</c> known.
    /// The stream is cut at 504-byte offsets, so every fragment but the last is full and the client reassembles the
    /// payloads before parsing. An empty zone sends one fragment with an empty payload.
    /// </para>
    /// <para>
    /// <b>Data.</b> Fields the zone does not own come from <see cref="IZoneEntityStateSource"/>; an entity the seam
    /// has no data for is skipped and one warning per call is logged. A mob whose type id does not fit the 16-bit
    /// wire field (above <see cref="ushort.MaxValue"/>) is skipped the same way. A player is dead (<c>isInDeadState</c>) in
    /// <see cref="ZoneSessionState.Dead"/> or <see cref="ZoneSessionState.Respawning"/>, with
    /// <c>respawnTicksRemaining = max(0, RespawnTick - tick)</c>; a ghost is
    /// <see cref="ZoneSessionState.Disconnected_SessionActive"/>.
    /// </para>
    /// <para>
    /// Main thread only. Buffers are allocated once in the constructor; a call allocates nothing unless it warns,
    /// provided the <see cref="IZoneEntityStateSource"/> implementation does not allocate either.
    /// Fragments are enqueued with <c>isCapExempt: true</c> (bulk transfer, ADR-004). The outbox may refuse one
    /// (full queue, unknown client); it logs that itself and, on a full queue, requests the client's disconnect, so
    /// the client never completes this snapshot. The builder does not see refusals and does not retry.
    /// </para>
    /// </remarks>
    public sealed class ZoneSnapshotBuilder
    {
        private const string LOG_PREFIX = "[ZoneSnapshotBuilder]";

        private readonly ZoneID _zone;
        private readonly ZoneSlotPool _pool;
        private readonly IZoneMobRoster _mobs;
        private readonly IZoneEntityStateSource _source;
        private readonly IServerTickSource _ticks;
        private readonly IClientMessageOutbox _outbox;
        private readonly byte[] _stream;
        private readonly byte[] _fragmentBody;

        /// <summary>Creates a builder for one zone.</summary>
        /// <param name="zone">The zone this process hosts; must not be <see cref="ZoneID.Invalid"/>.</param>
        /// <param name="pool">The slot pool whose rosters are walked.</param>
        /// <param name="mobs">The mob roster giving each mob's type.</param>
        /// <param name="source">The read seam for position, health and the other fields.</param>
        /// <param name="ticks">The tick source; its tick is the snapshot version.</param>
        /// <param name="outbox">Receives the fragments.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="zone"/> is the invalid (0) zone id.</exception>
        public ZoneSnapshotBuilder(ZoneID zone, ZoneSlotPool pool, IZoneMobRoster mobs, IZoneEntityStateSource source,
            IServerTickSource ticks, IClientMessageOutbox outbox)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            if (mobs == null)
            {
                throw new ArgumentNullException(nameof(mobs));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ticks == null)
            {
                throw new ArgumentNullException(nameof(ticks));
            }

            if (outbox == null)
            {
                throw new ArgumentNullException(nameof(outbox));
            }

            if (zone == ZoneID.Invalid)
            {
                throw new ArgumentException($"{LOG_PREFIX} zone must not be ZoneID.Invalid.", nameof(zone));
            }

            _zone = zone;
            _pool = pool;
            _mobs = mobs;
            _source = source;
            _ticks = ticks;
            _outbox = outbox;
            _stream = new byte[pool.MaxPlayers * EntityState.PlayerMaxBodySize + pool.MaxMobs * EntityState.MobBodySize];
            _fragmentBody = new byte[ZoneStateSnapshotFragment.MaxMessageBodyBytes];
        }

        /// <summary>
        /// Builds the snapshot of the zone as of the current tick and enqueues all its fragments for
        /// <paramref name="clientId"/>. The joining client's own slot is included.
        /// </summary>
        /// <param name="clientId">The connection that receives the snapshot.</param>
        /// <returns>
        /// The number of fragments handed to the outbox (at least 1), which is <c>totalFragments</c>. A fragment the
        /// outbox refuses is still counted (see the class remarks).
        /// </returns>
        public int SendSnapshot(uint clientId)
        {
            uint tick = _ticks.ServerTickNumber;
            bool warned = false;
            int length = AppendPlayers(tick, 0, ref warned);
            length = AppendMobs(length, ref warned);
            return SendFragments(clientId, tick, length);
        }

        // Writes one entry per player in roster order from offset; returns the new stream length.
        private int AppendPlayers(uint tick, int length, ref bool warned)
        {
            IReadOnlyList<EntityID> players = _pool.GetPlayerEntityIDsInZone(_zone);
            for (int i = 0; i < players.Count; i++)
            {
                EntityID id = players[i];
                if (!_pool.TryGetPlayer(id, out ZonePlayerSlot slot) || !_source.TryGetPlayerState(id, out ZonePlayerStateView v))
                {
                    WarnSkipped(id, "player", "has no state data", ref warned);
                    continue;
                }

                bool dead = slot.State == ZoneSessionState.Dead || slot.State == ZoneSessionState.Respawning;
                uint remaining = dead && v.RespawnTick > tick ? v.RespawnTick - tick : 0u;
                EntityState entry = EntityState.CreatePlayer(id, v.PosX, v.PosY, v.PosZ, v.RotX, v.RotY, v.RotZ, v.RotW,
                    v.CurrentHp, v.MaxHp, v.IsInCombat, slot.State == ZoneSessionState.Disconnected_SessionActive,
                    v.CycleTimer, v.FacingAngle, v.CharacterName, v.Level, v.CurrentMp, v.MaxMp,
                    v.EquipmentAppearanceFlags, dead, remaining);
                length += EntityStateCodec.WriteBody(new Span<byte>(_stream, length, _stream.Length - length), in entry);
            }

            return length;
        }

        // Writes one entry per mob in roster order after the players; returns the new stream length.
        private int AppendMobs(int length, ref bool warned)
        {
            IReadOnlyList<EntityID> mobIds = _pool.GetMobEntityIDsInZone(_zone);
            for (int i = 0; i < mobIds.Count; i++)
            {
                EntityID id = mobIds[i];
                if (!_mobs.TryGetMob(id, out MobTypeID type, out _) || !_source.TryGetMobState(id, out ZoneMobStateView v))
                {
                    WarnSkipped(id, "mob", "has no state data", ref warned);
                    continue;
                }

                if (type.RawValue > ushort.MaxValue)
                {
                    WarnSkipped(id, "mob", "has a type id above the 16-bit wire field", ref warned);
                    continue;
                }

                EntityState entry = EntityState.CreateMob(id, v.PosX, v.PosY, v.PosZ, v.RotX, v.RotY, v.RotZ, v.RotW,
                    v.CurrentHp, v.MaxHp, v.IsInCombat, v.CycleTimer, v.FacingAngle, (ushort)type.RawValue);
                length += EntityStateCodec.WriteBody(new Span<byte>(_stream, length, _stream.Length - length), in entry);
            }

            return length;
        }

        // Cuts the stream at 504-byte offsets and enqueues each fragment; returns totalFragments.
        private int SendFragments(uint clientId, uint tick, int length)
        {
            int payloadMax = ZoneStateSnapshotFragment.MaxPayloadBytes;
            int total = Math.Max(1, (length + payloadMax - 1) / payloadMax);
            for (int index = 0; index < total; index++)
            {
                int offset = index * payloadMax;
                int chunk = Math.Min(payloadMax, length - offset);
                var header = new ZoneStateSnapshotFragment(tick, (ushort)total, (ushort)index);
                int size = ZoneStateSnapshotFragmentCodec.WriteBody(_fragmentBody, in header,
                    new ReadOnlySpan<byte>(_stream, offset, chunk));
                _outbox.Enqueue(clientId, ZoneStateSnapshotFragment.MessageTypeId,
                    new ReadOnlySpan<byte>(_fragmentBody, 0, size), isCapExempt: true);
            }

            return total;
        }

        // At most one warning per SendSnapshot call; the string is built only here.
        private void WarnSkipped(EntityID id, string kind, string reason, ref bool warned)
        {
            if (warned)
            {
                return;
            }

            warned = true;
            Debug.LogWarning($"{LOG_PREFIX} SnapshotEntitySkipped: {kind} {id} in {_zone} {reason}; left out of the snapshot (one warning per call).");
        }
    }
}
