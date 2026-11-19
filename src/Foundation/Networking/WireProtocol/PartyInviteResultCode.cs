namespace IronGrind.Networking
{
    /// <summary>
    /// Result code of <see cref="PartyInviteResult"/> (networking-wire-protocol.md Party System Messages).
    /// Values are fixed wire values.
    /// </summary>
    public enum PartyInviteResultCode : byte
    {
        /// <summary>The invite was delivered.</summary>
        Sent = 0,

        /// <summary>The invite was queued behind a pending one (CR-PS-4).</summary>
        Queued = 1,

        /// <summary>The party is full.</summary>
        PartyFull = 2,

        /// <summary>The target is outside the level range.</summary>
        LevelRange = 3,

        /// <summary>The target is already in a party.</summary>
        AlreadyInParty = 4,

        /// <summary>The target character was not found.</summary>
        TargetNotFound = 5,

        /// <summary>The sender is not the party leader.</summary>
        NotLeader = 6,

        /// <summary>The invite expired.</summary>
        Expired = 7,

        /// <summary>The target declined.</summary>
        Declined = 8
    }
}
