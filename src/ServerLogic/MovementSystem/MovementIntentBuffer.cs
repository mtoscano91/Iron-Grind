using System;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;
using IronGrind.StatusEffects;

namespace IronGrind.MovementSystem
{
    /// <summary>
    /// The per-slot buffer of the latest accepted <see cref="MovementIntentMessage"/> (Movement Story 004, CR-MOV-10).
    /// It is the inbound request handler of the type (<see cref="HandleMovementIntent"/>, registered by
    /// <see cref="MovementInboundRegistration"/>) and the <see cref="IMovementIntentSource"/> the movement tick reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two different drops.</b> The dispatcher's intake already drops a stale envelope <c>SequenceNumber</c>
    /// per connection and type (ADR-014 Decision 2, step 4) before this handler is reached. This handler adds the
    /// F-MOV-7 drop on the body <c>TickNumber</c>: discarded when <c>TickNumber &lt; DispatchTick - StaleTickTolerance</c>
    /// (written without uint underflow). A <c>TickNumber</c> ahead of the server is accepted. Stale drops are counted
    /// by nothing and logged nowhere (hot path).
    /// </para>
    /// <para>
    /// <b>Sender.</b> The guard chain has already checked that <c>SenderEntityId</c> belongs to the sending connection
    /// (AC-MOV-17); the handler only maps it to a slot through <see cref="IEntitySlotIndex"/>. An entity without a slot
    /// is dropped silently.
    /// </para>
    /// <para>
    /// <b>Last wins.</b> One entry per slot: a later accepted message overwrites an earlier one. Each entry records the
    /// dispatch tick of its write; <see cref="TryTakeIntent"/> returns it only when that equals the server tick (requests
    /// dispatch before the simulation within a tick) and clears the slot either way.
    /// </para>
    /// <para>Preallocated; allocation-free and not thread-safe (tick thread only, like the dispatcher).</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var buffer = new MovementIntentBuffer(tickSource, slotIndex, MovementTuningValues.Defaults);
    /// MovementInboundRegistration.Register(dispatcher, buffer);
    /// // movement tick:
    /// if (buffer.TryTakeIntent(slot, out MovementIntentMessage intent)) { /* apply */ }
    /// </code>
    /// </example>
    public sealed class MovementIntentBuffer : IMovementIntentSource
    {
        private const int SLOT_COUNT = ZoneSlotPool.MAX_PLAYERS_PER_ZONE;

        private readonly IServerTickSource _tickSource;
        private readonly IEntitySlotIndex _slotIndex;
        private readonly uint _staleTolerance;

        private readonly bool[] _hasIntent = new bool[SLOT_COUNT];
        private readonly MovementIntentMessage[] _intents = new MovementIntentMessage[SLOT_COUNT];
        private readonly uint[] _writeTicks = new uint[SLOT_COUNT];

        /// <summary>Creates the buffer.</summary>
        /// <param name="tickSource">Server tick; <see cref="TryTakeIntent"/> compares write ticks to it.</param>
        /// <param name="slotIndex">Maps an EntityID to its player slot.</param>
        /// <param name="tuning">Supplies <see cref="MovementTuningValues.StaleTickTolerance"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tickSource"/> or <paramref name="slotIndex"/> is null.</exception>
        public MovementIntentBuffer(IServerTickSource tickSource, IEntitySlotIndex slotIndex, MovementTuningValues tuning)
        {
            _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));
            _slotIndex = slotIndex ?? throw new ArgumentNullException(nameof(slotIndex));
            _staleTolerance = (uint)tuning.StaleTickTolerance;
        }

        /// <summary>
        /// Inbound handler of <see cref="MovementIntentMessage"/>. Drops (without logging) a body that does not decode,
        /// an F-MOV-7 stale <c>TickNumber</c>, and an entity without a slot; otherwise stores the message with its
        /// facing wrapped into 0-3599, replacing any earlier one for the slot.
        /// </summary>
        /// <param name="context">The request context; <c>DispatchTick</c> is the server tick of F-MOV-7.</param>
        /// <param name="body">The 10-byte body.</param>
        public void HandleMovementIntent(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            if (!MovementIntentMessageCodec.TryReadBody(body, out MovementIntentMessage message))
            {
                return;
            }

            uint tick = context.DispatchTick;
            if (tick > _staleTolerance && message.TickNumber < tick - _staleTolerance)
            {
                return;
            }

            if (!_slotIndex.TryGetSlotIndex(context.SenderEntityId, out int slot) || (uint)slot >= SLOT_COUNT)
            {
                return;
            }

            _intents[slot] = new MovementIntentMessage(message.DirX, message.DirZ,
                MovementIntentMessageCodec.NormalizeFacing(message.FacingAngle), message.TickNumber);
            _writeTicks[slot] = tick;
            _hasIntent[slot] = true;
        }

        /// <inheritdoc/>
        /// <remarks>An entry written on an earlier tick is cleared and reported as no intent. A slot outside range reports false.</remarks>
        public bool TryTakeIntent(int playerSlot, out MovementIntentMessage intent)
        {
            intent = default;
            if ((uint)playerSlot >= SLOT_COUNT || !_hasIntent[playerSlot])
            {
                return false;
            }

            _hasIntent[playerSlot] = false;
            if (_writeTicks[playerSlot] != _tickSource.ServerTickNumber)
            {
                return false;
            }

            intent = _intents[playerSlot];
            return true;
        }
    }
}
