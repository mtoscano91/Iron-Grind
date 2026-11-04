using System.Collections.Generic;
using System.Globalization;
using IronGrind.ItemDatabase;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The item database loaded at startup S2, read by S5 (Character Persistence Story 019). The composition looks for it
    /// on its <see cref="IZoneStaticDataLoader"/>; a loader that wraps another loader must forward it, or S5 falls back to
    /// an empty database.
    /// </summary>
    public interface IZoneItemData
    {
        /// <summary>The validated, ready item database; null until <c>LoadAndValidate</c> succeeded.</summary>
        IItemDatabase LoadedItemDatabase { get; }
    }

    /// <summary>
    /// Startup S2 loader for the item records (Character Persistence Story 019; ADR-009 Amendment 1 S2; item-database.md
    /// Rule 1 and the duplicate-<see cref="ItemID"/> edge case). Rejects a null or empty list and null entries, runs
    /// <see cref="ItemDefinitionValidator.ValidateBatch"/> (duplicate ids and invalid records are blocking), then
    /// initialises one <see cref="IronGrind.ItemDatabase.ItemDatabase"/>, then runs the loader for the remaining static
    /// data. A failure logs one <c>[ZoneServer] S2: &lt;reason&gt;</c> error and returns false (S-fail in every build).
    /// </summary>
    /// <remarks>
    /// <see cref="IronGrind.ItemDatabase.ItemDatabase.Initialize"/> keeps the last of two records with the same id without
    /// a word, so it is only called when the batch has no blocking issue. Non-blocking issues are logged as warnings.
    /// The records are owned by the scene; the loader never destroys them.
    /// </remarks>
    /// <example>
    /// <code>
    /// var loader = new ZoneItemDataLoader(items, new StandInZoneStaticDataLoader());
    /// if (!loader.LoadAndValidate()) { /* S-fail */ }
    /// IItemDatabase database = loader.LoadedItemDatabase;
    /// </code>
    /// </example>
    public sealed class ZoneItemDataLoader : IZoneStaticDataLoader, IZoneItemData
    {
        private const string LOG_PREFIX = "[ZoneServer] S2: ";

        private readonly IReadOnlyList<ItemDefinition> _items;
        private readonly IZoneStaticDataLoader _rest;

        /// <summary>Creates the loader.</summary>
        /// <param name="items">The item records (the host's serialized list); null or empty fails S2.</param>
        /// <param name="rest">The loader for the other static data, run after this one succeeds; null for none.</param>
        public ZoneItemDataLoader(IReadOnlyList<ItemDefinition> items, IZoneStaticDataLoader rest)
        {
            _items = items;
            _rest = rest;
        }

        /// <inheritdoc/>
        public IItemDatabase LoadedItemDatabase { get; private set; }

        /// <inheritdoc/>
        public bool LoadAndValidate()
        {
            // A failed reload must not leave the database of an earlier success exposed.
            LoadedItemDatabase = null;
            if (!TryBuild(_items, out IronGrind.ItemDatabase.ItemDatabase database, out string reason))
            {
                Debug.LogError(LOG_PREFIX + reason);
                return false;
            }

            if (_rest != null && !_rest.LoadAndValidate())
            {
                // S2 failed on the other static data: nothing is exposed.
                return false;
            }

            LoadedItemDatabase = database;
            return true;
        }

        /// <summary>
        /// Runs every S2 item check and, when all pass, builds the ready database. Logs one warning per non-blocking
        /// validation issue; logs nothing else.
        /// </summary>
        /// <param name="items">The item records.</param>
        /// <param name="database">The initialised database, or null.</param>
        /// <param name="reason">The first failed check, or null.</param>
        /// <returns>True when the database was built.</returns>
        public static bool TryBuild(IReadOnlyList<ItemDefinition> items, out IronGrind.ItemDatabase.ItemDatabase database,
            out string reason)
        {
            database = null;
            if (items == null)
            {
                reason = "item list missing";
                return false;
            }

            if (items.Count == 0)
            {
                reason = "item list is empty";
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null)
                {
                    reason = "item list entry " + i.ToString(CultureInfo.InvariantCulture) + " is null";
                    return false;
                }
            }

            ValidationResult result = ItemDefinitionValidator.ValidateBatch(items);
            if (!result.IsValid)
            {
                reason = "item validation failed: " + FirstBlocking(result);
                return false;
            }

            foreach (ValidationIssue issue in result.Issues)
            {
                Debug.LogWarning(LOG_PREFIX + "item validation: " + issue.Message);
            }

            database = new IronGrind.ItemDatabase.ItemDatabase();
            database.Initialize(items);
            reason = null;
            return true;
        }

        private static string FirstBlocking(ValidationResult result)
        {
            foreach (ValidationIssue issue in result.Issues)
            {
                if (issue.Severity != ValidationSeverity.Warning)
                {
                    return issue.Message;
                }
            }

            return "unknown";
        }
    }
}
