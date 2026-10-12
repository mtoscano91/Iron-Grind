using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;

namespace IronGrind.CharacterPersistence
{
    /// <summary>
    /// The seam between <see cref="CharacterPersistenceService"/> and the database (Character Persistence Story 002).
    /// </summary>
    /// <remarks>
    /// Story 014 writes the Npgsql implementation; <see cref="InMemoryCharacterRecordStore"/> serves tests and a
    /// server run with no database. The database owns the version predicate (ADR-006 Decision 2). The store stamps
    /// the timestamps (ADR-006's SQL writes <c>last_saved_at_utc = NOW()</c>). One update is one transaction
    /// (CR-CP-6). The purchase methods are Story 011's and are deliberately absent.
    /// </remarks>
    public interface ICharacterRecordStore
    {
        /// <summary>Fetches a character's record.</summary>
        /// <param name="id">The character.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The stored record, or <c>null</c> when the id has none (a miss is a result, not an exception).</returns>
        Task<CharacterRecord> FetchAsync(CharacterID id, CancellationToken ct);

        /// <summary>Inserts a new record with the <c>SaveVersion</c> it carries and stamps both timestamps.</summary>
        /// <param name="record">The record to insert.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when written; <c>false</c>, with nothing written, when the id already has a row.</returns>
        Task<bool> InsertAsync(CharacterRecord record, CancellationToken ct);

        /// <summary>
        /// Updates a record when the stored <c>SaveVersion</c> equals <paramref name="expectedSaveVersion"/>; the
        /// stored version becomes expected + 1 (F-CP-2).
        /// </summary>
        /// <param name="record">The record to write.</param>
        /// <param name="expectedSaveVersion">The version the caller last read.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The number of rows affected: 0 or 1.</returns>
        Task<int> UpdateAsync(CharacterRecord record, uint expectedSaveVersion, CancellationToken ct);
    }
}
