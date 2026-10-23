using System;
using System.Buffers.Binary;
using System.Text;

namespace IronGrind.Networking
{
    /// <summary>
    /// Shared helper for the "ushort UTF-8 byte count + UTF-8 bytes" string field of the Zone Instancing messages
    /// (Zone Instancing Story 006). Same algorithm as <see cref="ServerBroadcast_Enhancement9Codec"/> (which keeps its
    /// own private copy): encoding cuts to the longest prefix of whole characters that fits, surrogate pairs kept together.
    /// </summary>
    internal static class WireStringCodec
    {
        /// <summary>Width of the length prefix: a <see cref="ushort"/>, 2 bytes.</summary>
        internal const int LengthPrefixSize = 2;

        /// <summary>
        /// Writes <paramref name="value"/> (null = empty) at <paramref name="offset"/>, cut to at most
        /// <paramref name="maxBytes"/> UTF-8 bytes at a character boundary. Returns the offset after the string.
        /// Throws <see cref="ArgumentOutOfRangeException"/> if <paramref name="destination"/> is too small.
        /// </summary>
        internal static int WriteString(Span<byte> destination, int offset, string value, int maxBytes)
        {
            value ??= string.Empty;
            int charCount = CountCharsWithinByteLimit(value, maxBytes, out int byteCount);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, LengthPrefixSize), (ushort)byteCount);
            offset += LengthPrefixSize;
            Encoding.UTF8.GetBytes(value.AsSpan(0, charCount), destination.Slice(offset, byteCount));
            return offset + byteCount;
        }

        /// <summary>
        /// Reads a string at <paramref name="offset"/>. Returns <see langword="false"/> if the prefix does not fit, the
        /// length exceeds <paramref name="maxBytes"/>, or the body is shorter than the prefix claims.
        /// </summary>
        internal static bool TryReadString(ReadOnlySpan<byte> body, int offset, int maxBytes, out string value, out int nextOffset)
        {
            value = string.Empty;
            nextOffset = offset;

            if (body.Length - offset < LengthPrefixSize)
            {
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset, LengthPrefixSize));
            offset += LengthPrefixSize;

            if (length > maxBytes || body.Length - offset < length)
            {
                return false;
            }

            value = length == 0 ? string.Empty : Encoding.UTF8.GetString(body.Slice(offset, length));
            nextOffset = offset + length;
            return true;
        }

        private static int CountCharsWithinByteLimit(string value, int maxBytes, out int byteCount)
        {
            int chars = 0;
            byteCount = 0;

            while (chars < value.Length)
            {
                char c = value[chars];
                int unitChars = 1;
                int unitBytes;

                if (c < 0x80)
                {
                    unitBytes = 1;
                }
                else if (c < 0x800)
                {
                    unitBytes = 2;
                }
                else if (char.IsHighSurrogate(c) && chars + 1 < value.Length && char.IsLowSurrogate(value[chars + 1]))
                {
                    unitBytes = 4;
                    unitChars = 2;
                }
                else
                {
                    unitBytes = 3; // BMP char, or a lone surrogate (UTF-8 encoder substitutes U+FFFD, also 3 bytes)
                }

                if (byteCount + unitBytes > maxBytes)
                {
                    break;
                }

                byteCount += unitBytes;
                chars += unitChars;
            }

            return chars;
        }
    }
}
