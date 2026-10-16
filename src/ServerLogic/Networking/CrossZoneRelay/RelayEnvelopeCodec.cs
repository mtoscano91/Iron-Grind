using System;
using System.Globalization;

namespace IronGrind.Networking
{
    /// <summary>The result of <see cref="RelayEnvelopeCodec.TryDecode"/>.</summary>
    public enum RelayEnvelopeDecodeResult
    {
        /// <summary>The payload was decoded.</summary>
        Ok = 0,

        /// <summary>The payload does not start with <c>v1:</c>.</summary>
        UnknownVersion = 1,

        /// <summary>A field is missing, or the type field is not a decimal <c>ushort</c>.</summary>
        Malformed = 2,

        /// <summary>The origin field is not 16 hex digits.</summary>
        BadOrigin = 3,

        /// <summary>The body field is not valid base64.</summary>
        BadBase64 = 4,

        /// <summary>The body field looks like valid base64 but its decoded length exceeds the destination buffer.</summary>
        BodyTooLong = 5,
    }

    /// <summary>
    /// The relay envelope (ADR-015 Decision 5): ASCII text <c>v1:{messageTypeId}:{originId}:{base64(body)}</c> with the
    /// type in decimal, the origin as 16 lowercase hex digits and the body in standard base64. Shared by the
    /// publisher side (<see cref="Encode"/>) and the receiving check (<see cref="TryDecode"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="Encode"/> allocates (a string per relayed message, a few per hour) and is not for the tick path
    /// beyond the publish. <see cref="TryDecode"/> allocates nothing and never throws. Static and stateless, so
    /// thread-safe.
    /// </remarks>
    /// <example>
    /// <code>
    /// string payload = RelayEnvelopeCodec.Encode(0xE103, origin.Value, body);   // "v1:57603:0123456789abcdef:AAAAAAA="
    /// Span&lt;byte&gt; buffer = stackalloc byte[54];
    /// var result = RelayEnvelopeCodec.TryDecode(payload, buffer, out ushort type, out ulong from, out int length);
    /// </code>
    /// </example>
    public static class RelayEnvelopeCodec
    {
        /// <summary>The version prefix, including the separator.</summary>
        public const string VERSION_PREFIX = "v1:";

        private const int MAX_TYPE_DIGITS = 5;
        private const int FIXED_PART_LENGTH = 3 + MAX_TYPE_DIGITS + 1 + CrossZoneRelayOrigin.HEX_DIGITS + 1;

        /// <summary>The longest valid envelope for a body of at most <paramref name="maxBodyBytes"/> bytes.</summary>
        /// <param name="maxBodyBytes">The largest body any allowlist row permits.</param>
        /// <returns>The length in characters.</returns>
        public static int MaxEnvelopeLength(int maxBodyBytes)
        {
            return FIXED_PART_LENGTH + Base64Length(maxBodyBytes);
        }

        /// <summary>
        /// The destination buffer size that decodes any envelope no longer than <see cref="MaxEnvelopeLength"/>
        /// (it can be a little above <paramref name="maxBodyBytes"/>, because base64 pads to a multiple of 3 bytes).
        /// </summary>
        /// <param name="maxBodyBytes">The largest body any allowlist row permits.</param>
        /// <returns>The buffer size in bytes.</returns>
        public static int DecodeBufferSize(int maxBodyBytes)
        {
            return Base64Length(maxBodyBytes) / 4 * 3;
        }

        /// <summary>Builds the envelope text. Allocates.</summary>
        /// <param name="messageTypeId">The wire message type.</param>
        /// <param name="originId">The sender's origin id.</param>
        /// <param name="body">The body, exactly as the outbox takes it.</param>
        /// <returns>The payload string.</returns>
        public static string Encode(ushort messageTypeId, ulong originId, ReadOnlySpan<byte> body)
        {
            string base64 = Convert.ToBase64String(body);
            return VERSION_PREFIX
                + messageTypeId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + originId.ToString("x16", CultureInfo.InvariantCulture)
                + ":"
                + base64;
        }

        /// <summary>Parses the envelope into <paramref name="bodyBuffer"/>. Never throws; allocates nothing.</summary>
        /// <param name="payload">The payload text; null is malformed.</param>
        /// <param name="bodyBuffer">Receives the decoded body; see <see cref="DecodeBufferSize"/>.</param>
        /// <param name="messageTypeId">The type, valid only when the result is Ok.</param>
        /// <param name="originId">The origin, valid only when the result is Ok.</param>
        /// <param name="bodyLength">The decoded body length, valid only when the result is Ok.</param>
        /// <returns>Ok, or the reason the payload was refused.</returns>
        public static RelayEnvelopeDecodeResult TryDecode(
            string payload,
            Span<byte> bodyBuffer,
            out ushort messageTypeId,
            out ulong originId,
            out int bodyLength)
        {
            messageTypeId = 0;
            originId = 0;
            bodyLength = 0;
            if (payload == null)
            {
                return RelayEnvelopeDecodeResult.Malformed;
            }

            ReadOnlySpan<char> rest = payload.AsSpan();
            if (rest.Length < VERSION_PREFIX.Length || rest[0] != 'v' || rest[1] != '1' || rest[2] != ':')
            {
                return RelayEnvelopeDecodeResult.UnknownVersion;
            }

            rest = rest.Slice(VERSION_PREFIX.Length);
            int separator = rest.IndexOf(':');
            if (separator <= 0 || separator > MAX_TYPE_DIGITS)
            {
                return RelayEnvelopeDecodeResult.Malformed;
            }

            if (!ushort.TryParse(rest.Slice(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out ushort parsedType))
            {
                return RelayEnvelopeDecodeResult.Malformed;
            }

            rest = rest.Slice(separator + 1);
            separator = rest.IndexOf(':');
            if (separator < 0)
            {
                return RelayEnvelopeDecodeResult.Malformed;
            }

            if (!TryParseOrigin(rest.Slice(0, separator), out ulong parsedOrigin))
            {
                return RelayEnvelopeDecodeResult.BadOrigin;
            }

            ReadOnlySpan<char> base64 = rest.Slice(separator + 1);
            if (!IsBase64Alphabet(base64) || base64.Length % 4 != 0)
            {
                return RelayEnvelopeDecodeResult.BadBase64;
            }

            if (DecodedLength(base64) > bodyBuffer.Length)
            {
                return RelayEnvelopeDecodeResult.BodyTooLong;
            }

            if (!Convert.TryFromBase64Chars(base64, bodyBuffer, out int written))
            {
                return RelayEnvelopeDecodeResult.BadBase64;
            }

            messageTypeId = parsedType;
            originId = parsedOrigin;
            bodyLength = written;
            return RelayEnvelopeDecodeResult.Ok;
        }

        // Decoded size from the length (a multiple of 4) and its trailing '=' padding (at most two count).
        private static int DecodedLength(ReadOnlySpan<char> base64)
        {
            int padding = 0;
            if (base64.Length > 0 && base64[base64.Length - 1] == '=')
            {
                padding = base64.Length > 1 && base64[base64.Length - 2] == '=' ? 2 : 1;
            }

            return base64.Length / 4 * 3 - padding;
        }

        private static int Base64Length(int byteCount)
        {
            return (byteCount + 2) / 3 * 4;
        }

        private static bool TryParseOrigin(ReadOnlySpan<char> field, out ulong value)
        {
            value = 0;
            if (field.Length != CrossZoneRelayOrigin.HEX_DIGITS)
            {
                return false;
            }

            ulong result = 0;
            for (int i = 0; i < field.Length; i++)
            {
                char c = field[i];
                int digit;
                if (c >= '0' && c <= '9')
                {
                    digit = c - '0';
                }
                else if (c >= 'a' && c <= 'f')
                {
                    digit = c - 'a' + 10;
                }
                else if (c >= 'A' && c <= 'F')
                {
                    digit = c - 'A' + 10;
                }
                else
                {
                    return false;
                }

                result = (result << 4) | (uint)digit;
            }

            value = result;
            return true;
        }

        // Convert.TryFromBase64Chars skips white space; the envelope has none.
        private static bool IsBase64Alphabet(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                    || c == '+' || c == '/' || c == '=';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
