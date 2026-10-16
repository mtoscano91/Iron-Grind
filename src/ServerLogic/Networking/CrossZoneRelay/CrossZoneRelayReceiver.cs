using System;
using System.Threading;
using UnityEngine;

namespace IronGrind.Networking
{
    /// <summary>
    /// The receiving check of the relay (ADR-015 Decisions 3 to 5), shared by the in-memory test bus and the Npgsql
    /// listener's notification handler (Story 041). Given a payload string it applies, in order: the length limit,
    /// the envelope decode (version, fields, origin, base64), the allowlist, the type's <c>MaxBodyBytes</c> and the
    /// own-origin check, and then copies the body into the inbox and does nothing else. It calls no game code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Threading.</b> Called from one thread at a time (the listener thread; in tests, the bus); it owns a decode
    /// buffer that is not shared. It reads only the immutable allowlist and its origin id and writes only the inbox,
    /// so it never touches game state. <see cref="OnPayload"/> never throws.
    /// </para>
    /// <para>
    /// <b>Logging.</b> Every refusal except an own-origin echo and a full inbox is counted
    /// (<see cref="RefusedPayloadCount"/>) and logged under a deterministic throttle, so a flood of hostile NOTIFY
    /// payloads cannot flood the log from the listener thread. One interlocked counter per refusal reason: the first
    /// refusal of a reason logs one warning (<c>[CrossZoneRelayReceiver] PayloadDropped: reason=... length=...</c>,
    /// the payload length, never its text); after that only every <see cref="REFUSAL_LOG_EVERY"/>th refusal of that
    /// reason logs one summary line (<c>PayloadsDropped: reason=... count=N (logging every 100th)</c>, N the running
    /// total for the reason). An exception inside the check (reason ReceiverThrew) is throttled the same way. No clock
    /// is involved. An own-origin echo is counted (<see cref="OwnOriginDropCount"/>); a full inbox is counted and
    /// logged by the inbox's drain. <see cref="OnPayload"/> allocates only when it logs (off the tick thread).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var receiver = new CrossZoneRelayReceiver(allowlist, inbox, origin);
    /// bool queued = receiver.OnPayload(payloadFromNotification);
    /// </code>
    /// </example>
    public sealed class CrossZoneRelayReceiver
    {
        /// <summary>Only the first refusal of a reason, then every this-many-th one, is logged.</summary>
        public const int REFUSAL_LOG_EVERY = 100;

        private const string LOG_PREFIX = "[CrossZoneRelayReceiver]";

        private enum Reason
        {
            TooLong = 0,
            UnknownVersion,
            Malformed,
            BadOrigin,
            BadBase64,
            BodyTooLong,
            NotAllowlisted,
            ReceiverThrew,
            Count,
        }

        private readonly CrossZoneRelayAllowlist _allowlist;
        private readonly ICrossZoneRelayInbox _inbox;
        private readonly ulong _ownOriginId;
        private readonly int _maxEnvelopeLength;
        private readonly byte[] _decodeBuffer;
        private int _ownOriginDrops;
        private int _refusedPayloads;
        private readonly int[] _reasonCounts = new int[(int)Reason.Count];

        /// <summary>Creates the receiver.</summary>
        /// <param name="allowlist">The relay allowlist; not null.</param>
        /// <param name="inbox">Where accepted bodies go; not null.</param>
        /// <param name="origin">This process's origin id; envelopes carrying it are dropped; not null.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public CrossZoneRelayReceiver(CrossZoneRelayAllowlist allowlist, ICrossZoneRelayInbox inbox, CrossZoneRelayOrigin origin)
        {
            _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
            _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
            if (origin == null)
            {
                throw new ArgumentNullException(nameof(origin));
            }

            _ownOriginId = origin.Value;
            _maxEnvelopeLength = RelayEnvelopeCodec.MaxEnvelopeLength(allowlist.LargestMaxBodyBytes);
            _decodeBuffer = new byte[RelayEnvelopeCodec.DecodeBufferSize(allowlist.LargestMaxBodyBytes)];
        }

        /// <summary>Envelopes dropped because they carried this process's own origin id.</summary>
        public int OwnOriginDropCount => Volatile.Read(ref _ownOriginDrops);

        /// <summary>Payloads refused as too long, malformed, of an unknown version, not allowlisted or over their size (logged).</summary>
        public int RefusedPayloadCount => Volatile.Read(ref _refusedPayloads);

        /// <summary>Checks one payload and, if it passes, queues its body in the inbox. Never throws.</summary>
        /// <param name="payload">The envelope text; may be null or hostile.</param>
        /// <returns>True when the body was queued in the inbox.</returns>
        public bool OnPayload(string payload)
        {
            try
            {
                return Check(payload);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _refusedPayloads);
                int total = Interlocked.Increment(ref _reasonCounts[(int)Reason.ReceiverThrew]);
                if (total == 1)
                {
                    Debug.LogError($"{LOG_PREFIX} ReceiverThrew - {ex.GetType().Name}: {ex.Message}");
                }
                else if (total % REFUSAL_LOG_EVERY == 0)
                {
                    Debug.LogError($"{LOG_PREFIX} ReceiverThrewRepeatedly: count={total} (logging every {REFUSAL_LOG_EVERY}th)");
                }

                return false;
            }
        }

        private bool Check(string payload)
        {
            if (payload != null && payload.Length > _maxEnvelopeLength)
            {
                return Refuse(Reason.TooLong, payload.Length);
            }

            int length = payload == null ? 0 : payload.Length;
            RelayEnvelopeDecodeResult result = RelayEnvelopeCodec.TryDecode(
                payload, _decodeBuffer, out ushort typeId, out ulong originId, out int bodyLength);
            if (result != RelayEnvelopeDecodeResult.Ok)
            {
                return Refuse(ReasonOf(result), length);
            }

            if (!_allowlist.TryGetMaxBodyBytes(typeId, out int maxBody))
            {
                return Refuse(Reason.NotAllowlisted, length);
            }

            if (bodyLength > maxBody)
            {
                return Refuse(Reason.BodyTooLong, length);
            }

            if (originId == _ownOriginId)
            {
                Interlocked.Increment(ref _ownOriginDrops);
                return false;
            }

            return _inbox.TryAdd(typeId, new ReadOnlySpan<byte>(_decodeBuffer, 0, bodyLength));
        }

        private static Reason ReasonOf(RelayEnvelopeDecodeResult result)
        {
            switch (result)
            {
                case RelayEnvelopeDecodeResult.UnknownVersion: return Reason.UnknownVersion;
                case RelayEnvelopeDecodeResult.BadOrigin: return Reason.BadOrigin;
                case RelayEnvelopeDecodeResult.BadBase64: return Reason.BadBase64;
                case RelayEnvelopeDecodeResult.BodyTooLong: return Reason.BodyTooLong;
                default: return Reason.Malformed;
            }
        }

        private bool Refuse(Reason reason, int length)
        {
            Interlocked.Increment(ref _refusedPayloads);
            int total = Interlocked.Increment(ref _reasonCounts[(int)reason]);
            if (total == 1)
            {
                Debug.LogWarning($"{LOG_PREFIX} PayloadDropped: reason={reason} length={length}");
            }
            else if (total % REFUSAL_LOG_EVERY == 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} PayloadsDropped: reason={reason} count={total} (logging every {REFUSAL_LOG_EVERY}th)");
            }

            return false;
        }
    }
}
