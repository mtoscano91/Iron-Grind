#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Editor-only tool that authors the 34 MVP <see cref="ItemDefinition"/> assets
    /// (Story 004) from <see cref="MvpItemRecordData"/> into <c>Assets/data/items/</c>.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately kept out of a folder literally named "Editor" — this file
    /// must compile into the same assembly as <see cref="ItemDefinition"/> (there are
    /// no assembly definition files in this project yet; see tech-debt TD-002) so that
    /// its internal <c>SetForTesting</c> call remains visible. It is still Editor-only
    /// and excluded from player builds via the <c>#if UNITY_EDITOR</c> guard around the
    /// entire file, exactly like <see cref="ItemDefinition.SetForTesting"/> itself.</para>
    ///
    /// <para>Run via the Unity Editor menu: <c>IronGrind &gt; Item Database &gt; Seed MVP
    /// Item Records (Story 004)</c>. Existing assets at the target paths are overwritten
    /// on re-run, so this is safe to run again after editing <see cref="MvpItemRecordData"/>.</para>
    /// </remarks>
    internal static class ItemDatabaseSeeder
    {
        private const string EquipmentFolder = "Assets/data/items/equipment";
        private const string ConsumableFolder = "Assets/data/items/consumables";

        [MenuItem("IronGrind/Item Database/Seed MVP Item Records (Story 004)")]
        private static void SeedMvpItemRecords()
        {
            EnsureFolderExists(EquipmentFolder);
            EnsureFolderExists(ConsumableFolder);

            var records = MvpItemRecordData.BuildAll();
            int created = 0;

            foreach (var record in records)
            {
                string folder = record.ItemCategory == ItemCategory.Equipment ? EquipmentFolder : ConsumableFolder;
                string safeName = record.DisplayName.Replace(" ", "").Replace("(", "").Replace(")", "");
                string path = $"{folder}/{record.ItemId}_{safeName}.asset";

                string existingGuid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(existingGuid))
                {
                    AssetDatabase.DeleteAsset(path);
                }

                AssetDatabase.CreateAsset(record, path);
                created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ItemDatabaseSeeder] Created {created} item records ({EquipmentFolder}, {ConsumableFolder}).");

            RunValidationPass(records);
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        // Post-seed smoke check (story's "Validation Pass" requirement) — logs results to
        // the Console so the designer running this menu item sees pass/fail immediately.
        private static void RunValidationPass(System.Collections.Generic.List<ItemDefinition> records)
        {
            var result = ItemDefinitionValidator.ValidateBatch(records);

            int errors = 0, fatals = 0, warnings = 0;
            foreach (var issue in result.Issues)
            {
                switch (issue.Severity)
                {
                    case ValidationSeverity.Fatal: fatals++; Debug.LogError($"[ItemDatabaseSeeder] FATAL: {issue.Message}"); break;
                    case ValidationSeverity.Error: errors++; Debug.LogError($"[ItemDatabaseSeeder] ERROR: {issue.Message}"); break;
                    case ValidationSeverity.Warning: warnings++; Debug.LogWarning($"[ItemDatabaseSeeder] WARNING: {issue.Message}"); break;
                }
            }

            if (fatals == 0 && errors == 0)
            {
                Debug.Log($"[ItemDatabaseSeeder] Validation PASSED — 0 fatal, 0 errors, {warnings} warning(s). " +
                          "Record this result in production/qa/smoke-[date]-item-database.md per the story's Test Evidence requirement.");
            }
            else
            {
                Debug.LogError($"[ItemDatabaseSeeder] Validation FAILED — {fatals} fatal, {errors} error(s), {warnings} warning(s). " +
                                "Do not mark Story 004 Done until this is 0/0.");
            }
        }
    }
}

#endif
