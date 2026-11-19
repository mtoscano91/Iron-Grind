namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Typed outcome of a <see cref="PartyRegistry"/> operation (Party System Story 002). A value other than
    /// <see cref="Ok"/> means the operation changed no state.
    /// </summary>
    public enum PartyRegistryResult
    {
        /// <summary>The operation succeeded.</summary>
        Ok = 0,

        /// <summary>The party already holds MAX_PARTY_SIZE members.</summary>
        PartyFull = 1,

        /// <summary>The character is already a member of a real party.</summary>
        AlreadyInParty = 2,

        /// <summary>The target party is a solo party, which never accepts members.</summary>
        SoloPartyCannotAccept = 3,

        /// <summary>The operation needs a real party but was given a solo party id.</summary>
        SoloParty = 4,

        /// <summary>No party exists with the given id.</summary>
        UnknownParty = 5,

        /// <summary>The entity id is <c>EntityID.Invalid</c>.</summary>
        InvalidEntity = 6,

        /// <summary>The character has not been registered with <c>RegisterPlayer</c>.</summary>
        NotRegistered = 7,

        /// <summary>The character is already registered.</summary>
        AlreadyRegistered = 8,

        /// <summary>The character id is <c>CharacterID.Invalid</c>.</summary>
        InvalidCharacter = 9,

        /// <summary>The character is in a real party, but the operation needs a character in a solo party.</summary>
        NotSolo = 10,

        /// <summary>The character is not a member of the given party.</summary>
        NotMember = 11,

        /// <summary>The party id counter cannot issue the ids the operation needs (OQ-PS-2).</summary>
        IdsExhausted = 12,

        /// <summary>The entity is already registered to another character.</summary>
        EntityAlreadyRegistered = 13,
    }
}
