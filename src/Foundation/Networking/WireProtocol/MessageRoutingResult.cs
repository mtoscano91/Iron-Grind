using System;

namespace IronGrind.Networking
{
    /// <summary>
    /// The outcome of <see cref="MessageRoutingRegistry.ValidateAndRoute"/>: either the classified
    /// <see cref="MessageRoutingEntry"/> for a registered <c>MessageTypeID</c>, or (release builds
    /// only, EC-MCR-1) a fallback routing decision for an unregistered one.
    /// </summary>
    public readonly struct MessageRoutingResult : IEquatable<MessageRoutingResult>
    {
        /// <summary><see langword="true"/> if <see cref="Entry"/> is a real registry row; <see langword="false"/> if this is an EC-MCR-1 fallback.</summary>
        public readonly bool WasClassified;

        /// <summary>The <c>MessageTypeID</c> this result was computed for.</summary>
        public readonly ushort MessageTypeId;

        /// <summary>The registry row, if <see cref="WasClassified"/> is <see langword="true"/>; otherwise <see langword="default"/>.</summary>
        public readonly MessageRoutingEntry Entry;

        /// <summary>
        /// The channel this message should be routed on. Equals <see cref="Entry"/>.<see cref="MessageRoutingEntry.Channel"/>
        /// when classified; equals <see cref="NetworkChannel.ReliableUnordered"/> (the EC-MCR-1 safe default) otherwise.
        /// </summary>
        public readonly NetworkChannel RoutedChannel;

        private MessageRoutingResult(bool wasClassified, ushort messageTypeId, MessageRoutingEntry entry, NetworkChannel routedChannel)
        {
            WasClassified = wasClassified;
            MessageTypeId = messageTypeId;
            Entry = entry;
            RoutedChannel = routedChannel;
        }

        /// <summary>Builds a classified result from a real registry row.</summary>
        public static MessageRoutingResult Classified(MessageRoutingEntry entry)
            => new MessageRoutingResult(wasClassified: true, entry.MessageTypeId, entry, entry.Channel);

        /// <summary>
        /// Builds the EC-MCR-1 unclassified fallback result: routed to <see cref="NetworkChannel.ReliableUnordered"/> (R-U).
        /// </summary>
        public static MessageRoutingResult UnclassifiedFallback(ushort messageTypeId)
            => new MessageRoutingResult(wasClassified: false, messageTypeId, default, NetworkChannel.ReliableUnordered);

        /// <inheritdoc/>
        public bool Equals(MessageRoutingResult other)
            => WasClassified == other.WasClassified
            && MessageTypeId == other.MessageTypeId
            && Entry.Equals(other.Entry)
            && RoutedChannel == other.RoutedChannel;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is MessageRoutingResult other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            int h = WasClassified.GetHashCode();
            h = (h * 397) ^ MessageTypeId.GetHashCode();
            h = (h * 397) ^ Entry.GetHashCode();
            h = (h * 397) ^ RoutedChannel.GetHashCode();
            return h;
        }

        /// <summary>Returns true if both results have identical field values.</summary>
        public static bool operator ==(MessageRoutingResult left, MessageRoutingResult right) => left.Equals(right);

        /// <summary>Returns true if the results differ in any field.</summary>
        public static bool operator !=(MessageRoutingResult left, MessageRoutingResult right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString()
            => $"MessageRoutingResult(Id=0x{MessageTypeId:X4}, Classified={WasClassified}, RoutedChannel={RoutedChannel})";
    }
}
