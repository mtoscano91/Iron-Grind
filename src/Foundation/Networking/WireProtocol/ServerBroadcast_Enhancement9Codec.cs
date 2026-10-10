using System;
using System.Buffers.Binary;
using System.Text;

namespace IronGrind.Networking
{
    /// <summary>
    /// Body-level codec for <see cref="ServerBroadcast_Enhancement9"/>. Strings are written as a little-endian
    /// <see cref="ushort"/> byte count plus UTF-8 bytes. A string longer than
    /// <see cref="ServerBroadcast_Enhancement9.MaxStringBytes"/> UTF-8 bytes is cut to the longest prefix of whole
    /// characters (surrogate pairs kept together) that fits. The only allocation is the decoded strings.
    /// </summary>
    public static class ServerBroadcast_Enhancement9Codec
    {
        /// <summary>
        /// Writes the body at offset 0 of <paramref name="destination"/> and returns the number of bytes written
        /// (4 to 52). A <see langword="null"/> string is written as empty.
        /// </summary>
        /// <param name="destination">At least <see cref="ServerBroadcast_Enhancement9.MaxBodySize"/> bytes.</param>
        /// <param name="playerName">The player's display name (cut to 24 UTF-8 bytes).</param>
        /// <param name="itemName">The item's display name (cut to 24 UTF-8 bytes).</param>
        public static int WriteBody(Span<byte> destination, string playerName, string itemName)
        {
            int offset = WriteString(destination, 0, playerName);
            offset = WriteString(destination, offset, itemName);
            return offset;
        }

        /// <summary>
        /// Attempts to decode the body. Returns <see langword="false"/> (strings empty) if the body is shorter than
        /// its own length prefixes claim, or if a prefix exceeds <see cref="ServerBroadcast_Enhancement9.MaxStringBytes"/>
        /// (malformed). Extra trailing bytes are ignored.
        /// </summary>
        public static bool TryReadBody(ReadOnlySpan<byte> body, out string playerName, out string itemName)
        {
            playerName = string.Empty;
            itemName = string.Empty;

            if (!TryReadString(body, 0, out string first, out int next) ||
                !TryReadString(body, next, out string second, out _))
            {
                return false;
            }

            playerName = first;
            itemName = second;
            return true;
        }

        private static int WriteString(Span<byte> destination, int offset, string value)
        {
            value ??= string.Empty;
            int charCount = CountCharsWithinByteLimit(value, ServerBroadcast_Enhancement9.MaxStringBytes, out int byteCount);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, ServerBroadcast_Enhancement9.LengthPrefixSize), (ushort)byteCount);
            offset += ServerBroadcast_Enhancement9.LengthPrefixSize;
            Encoding.UTF8.GetBytes(value.AsSpan(0, charCount), destination.Slice(offset, byteCount));
            return offset + byteCount;
        }

        private static bool TryReadString(ReadOnlySpan<byte> body, int offset, out string value, out int nextOffset)
        {
            value = string.Empty;
            nextOffset = offset;

            if (body.Length - offset < ServerBroadcast_Enhancement9.LengthPrefixSize)
            {
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(offset, ServerBroadcast_Enhancement9.LengthPrefixSize));
            offset += ServerBroadcast_Enhancement9.LengthPrefixSize;

            if (length > ServerBroadcast_Enhancement9.MaxStringBytes || body.Length - offset < length)
            {
                return false;
            }

            value = length == 0 ? string.Empty : Encoding.UTF8.GetString(body.Slice(offset, length));
            nextOffset = offset + length;
            return true;
        }

        /// <summary>
        /// Returns how many UTF-16 chars of <paramref name="value"/> (whole characters only) encode to at most
        /// <paramref name="maxBytes"/> UTF-8 bytes, and that encoded byte count.
        /// </summary>
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
