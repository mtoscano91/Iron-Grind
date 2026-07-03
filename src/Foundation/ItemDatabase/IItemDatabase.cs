#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Read-only public contract for the runtime Item Database. Inject this interface
    /// into any system that needs to look up item definitions — never depend on the
    /// concrete <see cref="ItemDatabase"/> class directly.
    /// </summary>
    /// <remarks>
    /// <para>Lifecycle contract:</para>
    /// <list type="bullet">
    ///   <item>Check <see cref="IsReady"/> before querying. All query methods return safe
    ///   empty/null values and log a dev-build error when called before
    ///   <c>Initialize()</c> completes.</item>
    ///   <item>Subscribe to <see cref="OnDatabaseReady"/> to be notified when the database
    ///   becomes ready. If already ready at subscription time, the handler is invoked
    ///   synchronously before the assignment expression returns (late-subscriber pattern).</item>
    /// </list>
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// public class InventorySystem
    /// {
    ///     private readonly IItemDatabase _db;
    ///
    ///     public InventorySystem(IItemDatabase db) => _db = db;
    ///
    ///     public void DisplayItem(ItemID id)
    ///     {
    ///         if (_db.TryGetItem(id, out var def))
    ///             ShowTooltip(def.DisplayName);
    ///     }
    /// }
    /// </code>
    /// </remarks>
    public interface IItemDatabase
    {
        /// <summary>
        /// Returns <c>true</c> after <c>Initialize()</c> has completed successfully.
        /// All query methods return safe defaults and log a dev-build error when this
        /// is <c>false</c>.
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// Fired exactly once when <c>Initialize()</c> completes. If already ready at
        /// subscription time, the handler is invoked synchronously before the <c>+=</c>
        /// expression returns. A second <c>Initialize()</c> call does not re-fire the event.
        /// </summary>
        event Action OnDatabaseReady;

        /// <summary>
        /// Returns the <see cref="ItemDefinition"/> for the given <paramref name="id"/>, or
        /// <c>null</c> if no item with that ID is registered.
        /// </summary>
        /// <param name="id">
        /// The item identifier to look up. <see cref="ItemID.Invalid"/> (<c>uint 0</c>) is a
        /// valid sentinel meaning "no item" — returns <c>null</c> silently with no error logged.
        /// </param>
        /// <returns>The matching definition, or <c>null</c>.</returns>
        /// <remarks>
        /// Returns <c>null</c> and logs a dev-build error if called before
        /// <c>Initialize()</c> (i.e., <see cref="IsReady"/> is <c>false</c>).
        /// </remarks>
        ItemDefinition? GetItem(ItemID id);

        /// <summary>
        /// Attempts to retrieve the <see cref="ItemDefinition"/> for the given
        /// <paramref name="id"/>.
        /// </summary>
        /// <param name="id">
        /// The item identifier to look up. <see cref="ItemID.Invalid"/> returns
        /// <c>false</c> with <paramref name="item"/> set to <c>null</c> — no error logged.
        /// </param>
        /// <param name="item">
        /// The matching definition when this method returns <c>true</c>; otherwise <c>null</c>.
        /// </param>
        /// <returns>
        /// <c>true</c> if a definition was found; <c>false</c> if the ID is invalid, unknown,
        /// or the database is not yet initialised.
        /// </returns>
        bool TryGetItem(ItemID id, out ItemDefinition? item);

        /// <summary>
        /// Returns all registered items belonging to <paramref name="category"/>.
        /// </summary>
        /// <param name="category">The category to filter by.</param>
        /// <returns>
        /// A read-only list of matching definitions. Returns an empty list (never <c>null</c>)
        /// when no items match, the category is unknown, or the database is not yet initialised.
        /// </returns>
        /// <remarks>
        /// Logs a dev-build error and returns empty if <paramref name="category"/> is not a
        /// defined <see cref="ItemCategory"/> value, or if the database is not yet initialised.
        /// </remarks>
        IReadOnlyList<ItemDefinition> GetItemsByCategory(ItemCategory category);
    }
}
