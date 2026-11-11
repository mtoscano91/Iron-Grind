using System;
using System.Collections.Generic;
using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.Gateway.Link;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.ServerLogic.ZoneServer;
using UnityEngine;

namespace IronGrind.ServerLogic.Gateway
{
    /// <summary>
    /// The tick-thread half of the zone's gateway link (Gateway Story 011): drains the inbox at tick start
    /// (<see cref="IZoneGatewayLinkStep"/>, pipeline step 1b), sends <c>PlayerCount</c> / <c>ZoneDraining</c> at the end of
    /// phase 3 (<see cref="IZoneSimulationStep"/>, registered LAST), and publishes the immutable live-account snapshot the
    /// link thread's registration callback reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> <see cref="Drain"/>, <see cref="Run"/> and <see cref="RefreshSnapshot"/> run on the tick thread (S10
    /// also runs there). <see cref="Snapshot"/> is the only member the link thread reads: a <c>volatile</c> reference to an
    /// array that is never modified after publication. It is refreshed at the end of <see cref="Drain"/> and at the
    /// start of <see cref="Run"/>, and rebuilt (one allocation) only when the live set changes. The comparison is
    /// order-sensitive, so the same set in a new order (dictionary or roster reorder) republishes once; harmless. The
    /// <c>PlayerCount</c> sent right after registration covers any change made between two refreshes.
    /// </para>
    /// <para>
    /// <b>PlayerCount.</b> Sent when <see cref="ZoneInstancingWiring.SessionCount"/> (occupied + reserved) differs from the
    /// last sent value, at most once per tick, and only while the link is connected (so a count lost to a down link is
    /// never recorded as sent). Each new <c>LinkUpCount</c> (first registration and every re-registration) sends
    /// <c>ZoneDraining</c> first when the zone is not Active (<c>RegisterZone</c> carries no state; the gateway marks a
    /// registered zone Active), then <c>PlayerCount</c> unconditionally.
    /// </para>
    /// </remarks>
    internal sealed class GatewayLinkPump : IZoneGatewayLinkStep, IZoneSimulationStep
    {
        private const string LOG_PREFIX = "[GatewayLinkPump]";

        private readonly IGatewayLinkChannel _channel;
        private readonly IGatewayLinkOutbox _outbox;
        private readonly ZoneInstancingWiring _wiring;
        private readonly List<AccountID> _scratch = new List<AccountID>();
        private volatile AccountID[] _snapshot = new AccountID[0];
        private int _seenLinkUp;
        private int _lastSentCount = -1;

        /// <summary>Creates the pump.</summary>
        /// <param name="channel">The link channel (inbox and LinkUp counter).</param>
        /// <param name="outbox">Where <c>PlayerCount</c> and <c>ZoneDraining</c> go (the same outbox the handlers use).</param>
        /// <param name="wiring">The zone wiring whose handlers, session count and lifecycle the pump uses.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public GatewayLinkPump(IGatewayLinkChannel channel, IGatewayLinkOutbox outbox, ZoneInstancingWiring wiring)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _wiring = wiring ?? throw new ArgumentNullException(nameof(wiring));
        }

        /// <summary>The latest published live accounts (occupied players plus reserved admissions). Safe to read from any thread; never modify.</summary>
        public AccountID[] Snapshot => _snapshot;

        /// <summary>Player capacity of the zone (the pool's size).</summary>
        public int Capacity => _wiring.Pool.MaxPlayers;

        /// <summary>
        /// Recomputes the live accounts and publishes a new snapshot only if the set differs from the published one.
        /// Tick thread only. Allocates only on change.
        /// </summary>
        public void RefreshSnapshot()
        {
            _scratch.Clear();
            ZoneSlotPool pool = _wiring.Pool;
            IReadOnlyList<EntityID> players = pool.GetPlayerEntityIDsInZone(pool.Zone);
            for (int i = 0; i < players.Count; i++)
            {
                if (pool.TryGetPlayer(players[i], out ZonePlayerSlot slot) && slot.Account != AccountID.Invalid)
                {
                    _scratch.Add(slot.Account);
                }
            }

            _wiring.Admitted.CopyReservedAccounts(_scratch);

            AccountID[] published = _snapshot;
            bool same = published.Length == _scratch.Count;
            for (int i = 0; same && i < published.Length; i++)
            {
                same = published[i] == _scratch[i];
            }

            if (!same)
            {
                _snapshot = _scratch.ToArray();
            }
        }

        /// <inheritdoc/>
        public void Drain(uint currentTick)
        {
            while (_channel.TryDequeue(out GatewayToZoneMessage message))
            {
                try
                {
                    switch (message.Type)
                    {
                        case GatewayLinkMessageType.Admit:
                            _wiring.GatewayAdmit.Handle(message.Admit.RequestId, message.Admit.AccountId,
                                message.Admit.CharacterId, message.Admit.Token, currentTick);
                            break;
                        case GatewayLinkMessageType.Evict:
                            _wiring.GatewayEvict.Handle(message.Evict.RequestId, message.Evict.AccountId, currentTick);
                            break;
                        case GatewayLinkMessageType.Reconnect:
                            _wiring.GatewayReconnect.Handle(message.Reconnect.RequestId, message.Reconnect.AccountId,
                                message.Reconnect.Token, currentTick);
                            break;
                        default:
                            Debug.LogWarning($"{LOG_PREFIX} unexpected inbox message type {message.Type}; ignored.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{LOG_PREFIX} {message.Type} handling failed ({ex.GetType().Name}); the drain continues.\n{ex}");
                }
            }

            // An Admit accepted above is in the snapshot before this tick's simulation, so a reconnect during the tick
            // already registers it (no allocation unless the live set changed).
            RefreshSnapshot();
        }

        /// <inheritdoc/>
        public void Run(uint currentTick, float deltaTime)
        {
            RefreshSnapshot();

            int linkUp = _channel.LinkUpCount;
            int sessions = _wiring.SessionCount;
            ushort count = (ushort)Math.Max(0, Math.Min(sessions, ushort.MaxValue));

            if (linkUp != _seenLinkUp)
            {
                _seenLinkUp = linkUp;
                if (_wiring.Lifecycle.State != ZoneState.Active)
                {
                    _outbox.EnqueueZoneDraining();
                }

                _outbox.EnqueuePlayerCount(count);
                _lastSentCount = count;
                return;
            }

            if (!_channel.IsConnected || count == _lastSentCount)
            {
                return;
            }

            _outbox.EnqueuePlayerCount(count);
            _lastSentCount = count;
        }
    }
}
