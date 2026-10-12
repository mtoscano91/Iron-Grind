namespace IronGrind.CharacterPersistence
{
    /// <summary>Result of <see cref="ICharacterPersistence.LoadCharacter"/>: an outcome code and the character's last zone.</summary>
    public readonly struct CharacterLoadResult
    {
        /// <summary>The outcome of the load.</summary>
        public readonly CharacterLoadCode Code;

        /// <summary>The character's last zone; <see cref="ZoneID.Invalid"/> on first login (CR-CP-9). Read it only when <see cref="Code"/> is <see cref="CharacterLoadCode.Success"/>.</summary>
        public readonly ZoneID LastZoneID;

        /// <summary>Constructs a load result.</summary>
        /// <param name="code">The outcome of the load.</param>
        /// <param name="lastZoneID">The character's last zone.</param>
        public CharacterLoadResult(CharacterLoadCode code, ZoneID lastZoneID)
        {
            Code = code;
            LastZoneID = lastZoneID;
        }
    }
}
