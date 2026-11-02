using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.AutoAttackCombat
{
    /// <summary>
    /// The per-tick CycleBroadcast U-U packet (Auto-Attack Combat Story 008; wire protocol Path 2b-i,
    /// <c>TICK_BATCH_UU_CYCLE = 0x0103</c>). Every player in the zone (occupied player slot) gets one
    /// <see cref="CycleTimerBroadcast"/> entry, including the receiver's own entry; mobs never do. Every player
    /// receiver gets the same entry list, sent unreliably. IDLE players are included with their frozen action timer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tick thread only. Allocates nothing after construction: one <see cref="ClientBufferSet"/> per player slot
    /// (each receiver keeps its own buffer, mirroring the position step) and one entry list sized to the player
    /// capacity, cleared and refilled every tick.
    /// </para>
    /// <para>
    /// <b>Size.</b> 50 players is 12 + 50 x 10 = 512 bytes, exactly the writer cap. <b>Write failure.</b> If the writer
    /// throws (a pool-size misconfiguration above the cap) it has already logged its own CRITICAL error; the step
    /// catches the exception per client and logs one <c>SendThrew</c> error, so the other clients are still sent. The
    /// client's sequence number is consumed either way; entries are never truncated. A ghost (slot held, client unknown to the
    /// outbox) appears in others' packets but receives nothing.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var step = new CycleTimerBroadcastStep(combat, pool, sendSeam, outbox);
    /// step.Flush(currentTick);   // after the R-U batch flush, before the position broadcast
    /// </code>
    /// </example>
    public sealed class CycleTimerBroadcastStep : IZoneOutboundStep
    {
        private const string LOG_PREFIX = "[CycleTimerBroadcastStep]";

        /// <summary>The wire value of a completed cycle (full fraction).</summary>
        internal const ushort FULL_CYCLE_WIRE = 10000;

        private readonly AutoAttackCombatSystem _combat;
        private readonly ZoneSlotPool _pool;
        private readonly IClientMessageSendSeam _seam;
        private readonly ClientMessageOutbox _outbox;
        private readonly ClientBufferSet[] _buffers;
        private readonly List<CycleTimerBroadcast> _entries;

        /// <summary>Creates the step and pre-allocates every buffer.</summary>
        /// <param name="combat">Source of the action timers and cycle lengths.</param>
        /// <param name="pool">Slot pool: who is in the zone and which client id they use.</param>
        /// <param name="seam">The send seam (the same instance the outbox uses).</param>
        /// <param name="outbox">Provides each client's sequence counter (CCR-1).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public CycleTimerBroadcastStep(AutoAttackCombatSystem combat, ZoneSlotPool pool, IClientMessageSendSeam seam,
            ClientMessageOutbox outbox)
        {
            _combat = combat ?? throw new ArgumentNullException(nameof(combat));
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _seam = seam ?? throw new ArgumentNullException(nameof(seam));
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));

            int slots = pool.MaxPlayers;
            _buffers = new ClientBufferSet[slots];
            for (int i = 0; i < slots; i++)
            {
                _buffers[i] = new ClientBufferSet();
            }

            _entries = new List<CycleTimerBroadcast>(slots);
        }

        /// <summary>Normalizes an action timer to the 0-10,000 wire fraction of the cycle.</summary>
        /// <param name="actionTimerTicks">The timer in ticks; negative values count as 0.</param>
        /// <param name="cycleTicks">The cycle length in ticks; 0 or less yields 0 (never divides by zero).</param>
        /// <returns>0 to 10,000; 10,000 once the timer reaches the cycle length.</returns>
        public static ushort NormalizeCycleTimer(int actionTimerTicks, int cycleTicks)
        {
            if (cycleTicks <= 0)
            {
                return 0;
            }

            if (actionTimerTicks <= 0)
            {
                return 0;
            }

            if (actionTimerTicks >= cycleTicks)
            {
                return FULL_CYCLE_WIRE;
            }

            return (ushort)((long)actionTimerTicks * FULL_CYCLE_WIRE / cycleTicks);
        }

        /// <summary>Builds and sends this tick's cycle packets.</summary>
        /// <param name="currentTick">The tick number written into each packet.</param>
        public void Flush(uint currentTick)
        {
            int slots = _pool.MaxPlayers;
            _entries.Clear();
            for (int slot = 0; slot < slots; slot++)
            {
                if (!_pool.TryGetPlayerAtSlot(slot, out EntityID id, out uint _))
                {
                    continue;
                }

                ushort value = NormalizeCycleTimer(_combat.GetActionTimer(id), _combat.GetCycleTicks(id));
                _entries.Add(new CycleTimerBroadcast(id, value));
            }

            if (_entries.Count == 0)
            {
                return;
            }

            for (int slot = 0; slot < slots; slot++)
            {
                if (_pool.TryGetPlayerAtSlot(slot, out EntityID _, out uint clientId))
                {
                    SendTo(slot, clientId, currentTick);
                }
            }
        }

        private void SendTo(int slot, uint clientId, uint currentTick)
        {
            if (!_outbox.TryGetSequenceCounter(clientId, out ConnectionSequenceCounter counter))
            {
                return;
            }

            uint sequence = counter.IssueNext();
            byte[] buffer = _buffers[slot].CycleBroadcastBuffer;
            try
            {
                int bytes = CycleBroadcastPacketWriter.Write(new Span<byte>(buffer), sequence, currentTick, _entries, clientId);
                _seam.Send(clientId, new ReadOnlySpan<byte>(buffer, 0, bytes), NetworkChannel.Unreliable);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} SendThrew: clientId={clientId} - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
