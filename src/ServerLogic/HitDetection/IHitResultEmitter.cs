using IronGrind.CharacterStats;
using IronGrind.Networking;

namespace IronGrind.HitDetection
{
    /// <summary>
    /// Reports the outcome of one <see cref="HitDetector.CheckHit"/> call to the player involved
    /// (design/gdd/hit-detection.md CR-HD-10, CR-HD-11). Auto-Attack Combat and Enemy AI hold this interface and
    /// call it after every <c>CheckHit</c>; <c>CheckHit</c> itself stays pure. Hit Detection Story 004.
    /// </summary>
    /// <remarks>Tick thread only.</remarks>
    /// <example>
    /// <code>
    /// HitResult result = detector.CheckHit(attackerPos, targetPos, range, forward);
    /// emitter.Emit(attackerId, targetId, result);
    /// </code>
    /// </example>
    public interface IHitResultEmitter
    {
        /// <summary>
        /// Queues one <see cref="HitCheckResult"/> in the R-U batch of the player involved: the attacker's client
        /// when the attacker holds a player slot, otherwise the target's client when the target does. Nothing is
        /// queued when neither is a player. Never throws, given a collector that keeps the no-throw contract of
        /// <see cref="IRUBatchCollector.Enqueue"/>.
        /// </summary>
        /// <param name="attacker">The entity that attacked. Must not be <see cref="EntityID.Invalid"/>.</param>
        /// <param name="target">The entity that was attacked. Must not be <see cref="EntityID.Invalid"/>.</param>
        /// <param name="result">The <c>CheckHit</c> outcome.</param>
        void Emit(EntityID attacker, EntityID target, HitResult result);
    }
}
