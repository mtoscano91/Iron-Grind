using System;
using IronGrind.SkillSystem;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The request handler of <see cref="SkillCastRequest"/> (Skill System Story 004, ADR-014 Decision 3,
    /// <c>design/gdd/skill-system.md</c> CR-SK-1/CR-SK-2): decodes the 16-byte body, runs V-1 (caster is the sender)
    /// and V-2 (not stale) and forwards a valid request to <see cref="ISkillCastIntake"/>. A failed check sends no
    /// reply (AC-SK-43). V-0 and V-3..V-8 run in phase 3 (Story 005).
    /// </summary>
    /// <remarks>
    /// V-2 uses wrap-safe serial arithmetic: <c>(int)(serverTick - clientTick) &gt; tolerance</c>, so a client tick
    /// ahead of the server (negative difference) is never stale and the check survives <c>uint</c> wraparound.
    /// Warnings are throttled to one per server tick for each kind separately (malformed body, V-1, V-2), so a
    /// flooding client cannot spam the log and one kind never hides another. A suppressed request is still dropped.
    /// The tolerance is a constructor argument (the composition root reads <c>SkillSystemConfig</c>).
    /// </remarks>
    /// <example>
    /// <code>
    /// var handler = new SkillCastRequestHandler(intake, tickLoop, config.CastStaleTickTolerance);
    /// NetworkingCoreInboundRegistration.RegisterSkillInput(dispatcher, handler);
    /// </code>
    /// </example>
    public sealed class SkillCastRequestHandler
    {
        private const int KIND_MALFORMED = 0;
        private const int KIND_V1 = 1;
        private const int KIND_V2 = 2;
        private const int KIND_COUNT = 3;

        private readonly ISkillCastIntake _intake;
        private readonly IServerTickSource _tickSource;
        private readonly int _castStaleTickTolerance;
        private readonly bool[] _hasLogged = new bool[KIND_COUNT];
        private readonly uint[] _lastLogTick = new uint[KIND_COUNT];

        /// <summary>Creates the handler.</summary>
        /// <param name="intake">Receives accepted requests; not null.</param>
        /// <param name="tickSource">The current server tick; not null.</param>
        /// <param name="castStaleTickTolerance">V-2 tolerance in ticks (<c>CAST_STALE_TICK_TOLERANCE</c>), at least 0.</param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="castStaleTickTolerance"/> is negative.</exception>
        public SkillCastRequestHandler(ISkillCastIntake intake, IServerTickSource tickSource, int castStaleTickTolerance)
        {
            _intake = intake ?? throw new ArgumentNullException(nameof(intake));
            _tickSource = tickSource ?? throw new ArgumentNullException(nameof(tickSource));
            if (castStaleTickTolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(castStaleTickTolerance), castStaleTickTolerance, "must be >= 0");
            }

            _castStaleTickTolerance = castStaleTickTolerance;
        }

        /// <summary>
        /// Matches <see cref="InboundRequestHandler"/>. Malformed, spoofed (V-1) and stale (V-2) requests are dropped
        /// with a (throttled) warning and store nothing. Allocates nothing on the accept path.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <param name="body">The 16-byte body.</param>
        public void Handle(in InboundRequestContext context, ReadOnlySpan<byte> body)
        {
            uint serverTick = _tickSource.ServerTickNumber;

            if (!SkillCastRequestCodec.TryReadBody(body, out SkillCastRequest request))
            {
                if (ShouldLog(KIND_MALFORMED, serverTick))
                {
                    Debug.LogWarning($"[SkillCastRequestHandler] InboundMessageMalformed: clientId={context.ClientId} " +
                        $"messageType=0x{context.MessageTypeId:X4} - body length {body.Length}, expected {SkillCastRequest.BodySize} bytes.");
                }

                return;
            }

            // V-1: the claimed caster must be the connection's own entity.
            if (request.CasterEntityId != context.SenderEntityId.RawValue)
            {
                if (ShouldLog(KIND_V1, serverTick))
                {
                    Debug.LogWarning($"[SkillCastRequestHandler] SECURITY V-1 caster mismatch: clientId={context.ClientId} " +
                        $"sender={context.SenderEntityId.RawValue} claimedCaster={request.CasterEntityId} - dropped.");
                }

                return;
            }

            // V-2: stale when the client tick is behind the server tick by more than the tolerance. Wrap-safe serial
            // arithmetic: the uint difference is read as int, so a client tick ahead of the server gives a negative
            // value (never stale) and the check holds across uint wraparound.
            if (unchecked((int)(serverTick - request.ClientTickNumber)) > _castStaleTickTolerance)
            {
                if (ShouldLog(KIND_V2, serverTick))
                {
                    Debug.LogWarning($"[SkillCastRequestHandler] SECURITY V-2 stale cast: clientId={context.ClientId} " +
                        $"sender={context.SenderEntityId.RawValue} clientTick={request.ClientTickNumber} serverTick={serverTick} " +
                        $"tolerance={_castStaleTickTolerance} - dropped.");
                }

                return;
            }

            _intake.Submit(context.SenderEntityId, in request);
        }

        // One warning per server tick for each kind.
        private bool ShouldLog(int kind, uint tick)
        {
            if (_hasLogged[kind] && _lastLogTick[kind] == tick)
            {
                return false;
            }

            _hasLogged[kind] = true;
            _lastLogTick[kind] = tick;
            return true;
        }
    }
}
