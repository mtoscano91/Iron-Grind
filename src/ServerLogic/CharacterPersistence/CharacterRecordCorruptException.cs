using System;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// Thrown by an <see cref="ICharacterRecordStore"/> when a stored row cannot be turned into a
    /// <see cref="CharacterRecord"/> (for example a malformed <c>gear_slots</c> or <c>inventory_slots</c> JSON column).
    /// </summary>
    /// <remarks>
    /// <see cref="CharacterPersistenceService.LoadCharacter"/> maps this exception, and only this one, to
    /// <see cref="CharacterLoadCode.CorruptRecord"/>; every other store fault is <see cref="CharacterLoadCode.DatabaseError"/>
    /// (Character Persistence Story 014, ADR-006).
    /// </remarks>
    public sealed class CharacterRecordCorruptException : Exception
    {
        /// <summary>Creates the exception with a message naming the column and slot where known.</summary>
        /// <param name="message">What is wrong with the stored data.</param>
        public CharacterRecordCorruptException(string message)
            : base(message)
        {
        }

        /// <summary>Creates the exception with a message and the exception that caused it.</summary>
        /// <param name="message">What is wrong with the stored data.</param>
        /// <param name="innerException">The underlying cause.</param>
        public CharacterRecordCorruptException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
