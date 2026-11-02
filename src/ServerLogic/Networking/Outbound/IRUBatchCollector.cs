using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// Intake for the per-tick Path 2a Reliable-Unordered (R-U) batch (Hit Detection Story 003). Gameplay
    /// systems (Hit Detection Story 004 and later) hold this interface, not <see cref="RUBatchOutboundStep"/>.
    /// </summary>
    /// <remarks>
    /// Tick thread only. <see cref="Enqueue"/> accepts the eight opaque categories; <see cref="RUBatchCategory.DamageEvent"/>
    /// has typed intake in <see cref="EnqueueDamageEvent"/> and <see cref="RUBatchCategory.GoldSyncEvent"/> has typed
    /// intake elsewhere, so <see cref="Enqueue"/> refuses both.
    /// </remarks>
    /// <example>
    /// <code>
    /// collector.Enqueue(clientId, RUBatchCategory.HitCheckResult, HitCheckResult.MessageTypeId, bodyBytes);
    /// collector.EnqueueDamageEvent(clientId, in damageEvent);
    /// </code>
    /// </example>
    public interface IRUBatchCollector
    {
        /// <summary>
        /// Queues one sub-message for a client's batch this tick; copies <paramref name="body"/> before it
        /// returns. Never throws: a refused or dropped message is logged (or, for an unknown client, silently
        /// discarded) and nothing is queued.
        /// </summary>
        /// <param name="clientId">The connection that receives the batch.</param>
        /// <param name="category">One of the eight opaque categories.</param>
        /// <param name="messageTypeId">The wire message type stamped on the sub-message header.</param>
        /// <param name="body">The encoded body, at most <see cref="RUBatchWriter.MaxSingleOpaqueSubMessagePayloadBytes"/> bytes; valid only during the call.</param>
        void Enqueue(uint clientId, RUBatchCategory category, ushort messageTypeId, ReadOnlySpan<byte> body);

        /// <summary>
        /// Queues one <see cref="DamageEvent"/> for a client's batch this tick; the batch writes damage events first.
        /// Never throws: an unknown client is silently discarded, a full batch drops the event with a warning.
        /// </summary>
        /// <param name="clientId">The connection that receives the batch.</param>
        /// <param name="damageEvent">The event; copied before this returns.</param>
        void EnqueueDamageEvent(uint clientId, in DamageEvent damageEvent);
    }
}
