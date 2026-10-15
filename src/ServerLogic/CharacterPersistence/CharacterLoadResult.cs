namespace IronGrind.CharacterPersistence
{
    /// <summary>Result of <see cref="ICharacterPersistence.LoadCharacter"/>: an outcome code, the character's last zone and, on success, the validated record.</summary>
    public readonly struct CharacterLoadResult
    {
        /// <summary>The outcome of the load.</summary>
        public readonly CharacterLoadCode Code;

        /// <summary>The character's last zone; <see cref="ZoneID.Invalid"/> on first login (CR-CP-9). Read it only when <see cref="Code"/> is <see cref="CharacterLoadCode.Success"/>.</summary>
        public readonly ZoneID LastZoneID;

        /// <summary>
        /// The fetched and validated record; set only when <see cref="Code"/> is <see cref="CharacterLoadCode.Success"/>,
        /// <c>null</c> otherwise. The caller passes it to <c>CharacterPersistenceService.ApplyLoadedRecord</c> from its
        /// completion callback on the tick thread; it belongs to the caller from then on.
        /// </summary>
        public readonly CharacterRecord Record;

        /// <summary>Constructs a load result with no record.</summary>
        /// <param name="code">The outcome of the load.</param>
        /// <param name="lastZoneID">The character's last zone.</param>
        public CharacterLoadResult(CharacterLoadCode code, ZoneID lastZoneID)
            : this(code, lastZoneID, null)
        {
        }

        /// <summary>Constructs a load result.</summary>
        /// <param name="code">The outcome of the load.</param>
        /// <param name="lastZoneID">The character's last zone.</param>
        /// <param name="record">The validated record on success; <c>null</c> otherwise.</param>
        public CharacterLoadResult(CharacterLoadCode code, ZoneID lastZoneID, CharacterRecord record)
        {
            Code = code;
            LastZoneID = lastZoneID;
            Record = record;
        }
    }
}
