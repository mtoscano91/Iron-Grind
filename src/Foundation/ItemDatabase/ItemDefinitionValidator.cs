#nullable enable

using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ItemDatabase
{
    /// <summary>
    /// Severity of a single <see cref="ItemDefinitionValidator"/> finding.
    /// </summary>
    public enum ValidationSeverity : byte
    {
        /// <summary>Non-blocking. The record is accepted; a designer should confirm intent.</summary>
        Warning = 0,

        /// <summary>Blocking. The offending record is rejected at import.</summary>
        Error = 1,

        /// <summary>Blocking and load-aborting. Used for identity violations (e.g. duplicate
        /// or reserved <see cref="ItemID"/>) where continuing to process the batch would
        /// produce an ambiguous or silently-broken database.</summary>
        Fatal = 2,
    }

    /// <summary>
    /// A single validator finding: a severity plus a human-readable, designer-facing message.
    /// </summary>
    public readonly struct ValidationIssue
    {
        /// <summary>How serious this finding is.</summary>
        public ValidationSeverity Severity { get; }

        /// <summary>
        /// Human-readable description naming the offending item(s), the field, and the value.
        /// Intended for display in editor import logs — not parsed by any runtime system.
        /// </summary>
        public string Message { get; }

        /// <summary>Creates a new validation finding.</summary>
        public ValidationIssue(ValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        /// <inheritdoc/>
        public override string ToString() => $"[{Severity}] {Message}";
    }

    /// <summary>
    /// Accumulated outcome of validating one or more <see cref="ItemDefinition"/> records.
    /// </summary>
    /// <remarks>
    /// <see cref="IsValid"/> is <c>false</c> if any <see cref="ValidationSeverity.Error"/> or
    /// <see cref="ValidationSeverity.Fatal"/> issue was recorded. <see cref="ValidationSeverity.Warning"/>
    /// issues do not affect <see cref="IsValid"/> — the record is still accepted.
    /// </remarks>
    public sealed class ValidationResult
    {
        private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();
        private bool _hasBlockingIssue;

        /// <summary>All findings recorded during validation, in the order they were raised.</summary>
        public IReadOnlyList<ValidationIssue> Issues => _issues;

        /// <summary>
        /// <c>true</c> when no <see cref="ValidationSeverity.Error"/> or
        /// <see cref="ValidationSeverity.Fatal"/> issue is present. Warnings alone do not
        /// invalidate a record.
        /// </summary>
        public bool IsValid => !_hasBlockingIssue;

        internal void AddWarning(string message) =>
            _issues.Add(new ValidationIssue(ValidationSeverity.Warning, message));

        internal void AddError(string message)
        {
            _issues.Add(new ValidationIssue(ValidationSeverity.Error, message));
            _hasBlockingIssue = true;
        }

        internal void AddFatal(string message)
        {
            _issues.Add(new ValidationIssue(ValidationSeverity.Fatal, message));
            _hasBlockingIssue = true;
        }

        internal void Merge(ValidationResult other)
        {
            foreach (var issue in other._issues)
            {
                _issues.Add(issue);
                if (issue.Severity != ValidationSeverity.Warning)
                    _hasBlockingIssue = true;
            }
        }
    }

    /// <summary>
    /// Import-time data validator for <see cref="ItemDefinition"/> records. Enforces the
    /// schema invariants defined in the Item Database GDD (Detailed Rules, Edge Cases) —
    /// duplicate/reserved IDs, category-schema consistency, elemental field constraints,
    /// stat modifier authoring budget, and economy field sanity.
    /// </summary>
    /// <remarks>
    /// <para><b>Not a runtime hot path.</b> This runs during the editor import pipeline /
    /// data authoring tools, never during gameplay. Allocation and LINQ-free-but-not-obsessive
    /// code is acceptable here in a way it would not be inside <see cref="ItemDatabase"/> itself.</para>
    ///
    /// <para><b>IL2CPP-safe enum membership check for <see cref="StatID"/>:</b> per the Item
    /// Database schema reference, <c>Enum.IsDefined</c> is avoided for <see cref="StatID"/>
    /// because it boxes the value and can be unreliable under aggressive iOS stripping. Instead
    /// a <see cref="HashSet{T}"/> is built once from the non-generic
    /// <c>(StatID[])Enum.GetValues(typeof(StatID))</c> — the generic <c>Enum.GetValues&lt;T&gt;()</c>
    /// overload is deliberately not used here: no <c>.csproj</c> / <c>ProjectSettings.asset</c>
    /// exists yet in this project to confirm the pinned IL2CPP scripting runtime's exact BCL
    /// surface, so the always-available non-generic overload is used instead of a guess.</para>
    ///
    /// <para>Usage example:</para>
    /// <code>
    /// var result = ItemDefinitionValidator.ValidateBatch(allImportedRecords);
    /// if (!result.IsValid)
    /// {
    ///     foreach (var issue in result.Issues)
    ///         Debug.LogError(issue.ToString());
    ///     AbortImport();
    /// }
    /// </code>
    /// </remarks>
    public static class ItemDefinitionValidator
    {
        /// <summary>Authoring cap on <see cref="StatModifierEntry"/> entries per equipment item (Rule 7 / Tuning Knobs).</summary>
        private const int MaxStatModifiersPerItem = 2;

        /// <summary>Ceiling for <see cref="EquipmentData.ElementalDamage"/> at +0 enhancement (F-3).</summary>
        private const int MaxElementalDamage = 9_999;

        /// <summary>
        /// Baseline expected <see cref="ItemDefinition.SellPriceGold"/> per <see cref="GearTier"/>,
        /// per the GDD's F-1 economy formula. Used only for the ±5% advisory deviation check
        /// (AC-32) — never a hard constraint.
        /// </summary>
        private static float GetTierBasePrice(GearTier tier)
        {
            switch (tier)
            {
                case GearTier.Bronze: return 10f;
                case GearTier.Iron: return 30f;
                case GearTier.Steel: return 90f;
                case GearTier.DarkSteel: return 270f;
                default:
                    // GearTier.None is rejected by AC-10 before this is ever reached. Throwing
                    // here (rather than returning 0f) ensures a future untracked GearTier
                    // addition fails loudly at import time instead of silently producing an
                    // Infinity or NaN deviation.
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "No F-1 TierBasePrice defined for this GearTier value.");
            }
        }

        // Non-generic Enum.GetValues by design — see class remarks.
        private static readonly HashSet<StatID> ValidStatIds =
            new HashSet<StatID>((StatID[])Enum.GetValues(typeof(StatID)));

        /// <summary>
        /// Validates a single <see cref="ItemDefinition"/> in isolation. Does not check for
        /// duplicate <see cref="ItemDefinition.ItemId"/> values across other records — use
        /// <see cref="ValidateBatch"/> for that.
        /// </summary>
        /// <param name="item">The record to validate. Passing <c>null</c> yields a fatal result.</param>
        /// <returns>The accumulated findings for this record.</returns>
        public static ValidationResult ValidateRecord(ItemDefinition? item)
        {
            var result = new ValidationResult();

            if (item == null)
            {
                result.AddFatal("ItemDefinition reference is null.");
                return result;
            }

            ValidateItemId(item, result);
            ValidateSellPrice(item, result);

            switch (item.ItemCategory)
            {
                case ItemCategory.Equipment:
                    ValidateEquipment(item, result);
                    break;
                case ItemCategory.Consumable:
                    ValidateConsumable(item, result);
                    break;
                default:
                    result.AddError(
                        $"Item '{item.DisplayName}' (ID {item.ItemId}) has an undefined ItemCategory value ({(int)item.ItemCategory}).");
                    break;
            }

            return result;
        }

        /// <summary>
        /// Validates a full set of <see cref="ItemDefinition"/> records, including the
        /// cross-record duplicate-<see cref="ItemID"/> check (AC-3 / Rule 1).
        /// </summary>
        /// <param name="items">The full batch of records being imported. Null entries are skipped.</param>
        /// <returns>
        /// The accumulated findings for the whole batch. A duplicate <see cref="ItemID"/>
        /// produces a <see cref="ValidationSeverity.Fatal"/> issue naming both records and
        /// short-circuits the individual-record validation of the second record — the loader
        /// must abort without registering either record.
        /// </returns>
        public static ValidationResult ValidateBatch(IReadOnlyList<ItemDefinition?> items)
        {
            var result = new ValidationResult();
            var seen = new Dictionary<ItemID, ItemDefinition>();

            foreach (var item in items)
            {
                if (item == null) continue;

                if (seen.TryGetValue(item.ItemId, out var existing))
                {
                    result.AddFatal(
                        $"Duplicate ItemID {item.ItemId}: '{existing.DisplayName}' and '{item.DisplayName}' both reference this ID. Neither record will be registered.");
                    continue;
                }

                // Only track the first-seen record per ID as a duplicate-detection key —
                // do not overwrite, so the very first record with a given ID never itself
                // triggers a duplicate against a later, otherwise-unrelated validation pass.
                seen[item.ItemId] = item;

                result.Merge(ValidateRecord(item));
            }

            return result;
        }

        private static void ValidateItemId(ItemDefinition item, ValidationResult result)
        {
            // AC-26 / Rule 1, Rule 11: ItemID(0) is reserved as ItemID.Invalid.
            if (item.ItemId == ItemID.Invalid)
            {
                result.AddFatal(
                    $"Item '{item.DisplayName}' has ItemID(0), which is permanently reserved as ItemID.Invalid. Every item record must be assigned an ID in [1, 4294967295].");
            }
        }

        private static void ValidateSellPrice(ItemDefinition item, ValidationResult result)
        {
            // AC-25: SellPriceGold must never be negative.
            if (item.SellPriceGold < 0)
            {
                result.AddError(
                    $"Item '{item.DisplayName}' (ID {item.ItemId}) has SellPriceGold={item.SellPriceGold}; SellPriceGold must be >= 0.");
            }

            // AC-35: a zero sell price is valid but likely a data authoring error for any
            // non-gift item — advisory only, does not reject the record.
            if (item.SellPriceGold == 0)
            {
                result.AddWarning(
                    $"[ItemDatabase] '{item.DisplayName}': SellPriceGold=0 — item cannot be sold to NPCs. Confirm this is intentional.");
            }
        }

        private static void ValidateEquipment(ItemDefinition item, ValidationResult result)
        {
            var data = item.EquipmentData;
            if (data == null)
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has ItemCategory.Equipment but no EquipmentData.");
                return;
            }

            // AC-4 / Rule 5: equipment StackLimit must always be exactly 1.
            if (item.StackLimit != 1)
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has StackLimit={item.StackLimit}; equipment must always have StackLimit=1.");
            }

            // Rule 8 / Edge Cases: equipment must not carry consumable-only fields.
            if (item.ConsumableData != null)
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has ConsumableData set; equipment records must not populate consumable fields.");
            }

            // AC-7 / Rule 3: GearSlot must be one of the defined values.
            if (!Enum.IsDefined(typeof(GearSlot), data.GearSlot))
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has an undefined GearSlot value ({(int)data.GearSlot}).");
            }

            // AC-10 / Rule 4: equipment must be assigned exactly one real tier, never None.
            if (data.GearTier == GearTier.None)
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has GearTier.None; equipment must be assigned one of Bronze, Iron, Steel, or DarkSteel.");
            }
            else
            {
                // AC-32 / F-1: advisory check — SellPriceGold should track the tier's baseline
                // economy value within ±5%. Skipped for GearTier.None (already rejected above;
                // dividing by a zero baseline would be meaningless).
                float expected = GetTierBasePrice(data.GearTier);
                float deviation = Mathf.Abs(item.SellPriceGold - expected) / expected;
                if (deviation > 0.05f)
                {
                    result.AddWarning(
                        $"[ItemDatabase] '{item.DisplayName}': SellPriceGold={item.SellPriceGold} deviates from F-1 expected {expected}g ({deviation:P1}). Confirm or correct.");
                }
            }

            ValidateElementalFields(item, data, result);
            ValidateStatModifiers(item, data, result);
        }

        private static void ValidateElementalFields(ItemDefinition item, EquipmentData data, ValidationResult result)
        {
            bool isWeapon = data.GearSlot == GearSlot.Weapon;

            if (isWeapon)
            {
                // AC-12 / Rule 6, Edge Cases: a physical (None) weapon must not carry elemental damage.
                if (data.ElementType == ElementType.None && data.ElementalDamage > 0)
                {
                    result.AddError(
                        $"Weapon '{item.DisplayName}' (ID {item.ItemId}) has ElementType.None but ElementalDamage={data.ElementalDamage}; a physical weapon must have ElementalDamage=0.");
                }

                // AC-14 / F-3: ElementalDamage ceiling.
                if (data.ElementalDamage > MaxElementalDamage)
                {
                    result.AddError(
                        $"Weapon '{item.DisplayName}' (ID {item.ItemId}) has ElementalDamage={data.ElementalDamage}, exceeding the ceiling of {MaxElementalDamage}.");
                }
            }
            else
            {
                // AC-8 / Rule 6, Edge Cases: only the Weapon slot may carry elemental fields.
                if (data.ElementType != ElementType.None || data.ElementalDamage > 0)
                {
                    result.AddError(
                        $"Non-weapon equipment '{item.DisplayName}' (ID {item.ItemId}, slot {data.GearSlot}) has elemental fields set (ElementType={data.ElementType}, ElementalDamage={data.ElementalDamage}); only Weapon-slot items may carry elemental damage.");
                }
            }
        }

        private static void ValidateStatModifiers(ItemDefinition item, EquipmentData data, ValidationResult result)
        {
            var modifiers = data.StatModifiers;

            // AC-15 / Rule 7, Tuning Knobs: authoring budget cap.
            if (modifiers.Length > MaxStatModifiersPerItem)
            {
                result.AddError(
                    $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has {modifiers.Length} StatModifier entries; the authoring cap is {MaxStatModifiersPerItem}.");
            }

            foreach (var mod in modifiers)
            {
                // AC-41 / Rule 7, Edge Cases: StatId must be a defined StatID value.
                if (!ValidStatIds.Contains(mod.StatId))
                {
                    result.AddError(
                        $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has a StatModifierEntry referencing an undefined StatID value ({(int)mod.StatId}).");
                    continue;
                }

                // AC-11 / Rule 7, Edge Cases: zero-contribution entries are rejected.
                if (mod.FlatBonus == 0.0f)
                {
                    result.AddError(
                        $"Equipment item '{item.DisplayName}' (ID {item.ItemId}) has a StatModifierEntry for {mod.StatId} with FlatBonus=0.0; zero-contribution entries are not allowed.");
                }

                // AC-17: a negative FlatBonus is a valid authored penalty, but advisory-flagged
                // so a designer can confirm it wasn't a data-entry mistake.
                if (mod.FlatBonus < 0.0f)
                {
                    result.AddWarning(
                        $"[ItemDatabase] '{item.DisplayName}': StatModifierEntry({mod.StatId}) has FlatBonus={mod.FlatBonus} (negative penalty). Confirm this is intentional.");
                }
            }
        }

        private static void ValidateConsumable(ItemDefinition item, ValidationResult result)
        {
            var data = item.ConsumableData;
            if (data == null)
            {
                result.AddError(
                    $"Consumable item '{item.DisplayName}' (ID {item.ItemId}) has ItemCategory.Consumable but no ConsumableData.");
                return;
            }

            // AC-5 / Rule 8, Edge Cases: a consumable must not populate equipment fields at all.
            // GearSlot has no explicit "None" member — the presence of EquipmentData at all on
            // a consumable is itself the violation, regardless of which GearSlot/GearTier values
            // it happens to hold. This single check covers all three GDD-mandated sub-cases:
            // GearSlot set with GearTier==None, GearTier set with GearSlot at its default, and
            // both set simultaneously.
            if (item.EquipmentData != null)
            {
                result.AddError(
                    $"Consumable item '{item.DisplayName}' (ID {item.ItemId}) has EquipmentData set; consumables must not populate equipment fields (GearSlot/GearTier must be unset).");
            }

            // AC-22 / Rule 8, Edge Cases: a zero stack limit makes the item impossible to carry.
            if (item.StackLimit == 0)
            {
                result.AddError(
                    $"Consumable item '{item.DisplayName}' (ID {item.ItemId}) has StackLimit=0, making the item impossible to carry.");
            }

            // AC-21 / Rule 8, Edge Cases: a non-positive magnitude is a no-op or unsupported drain.
            if (data.EffectMagnitude <= 0f)
            {
                result.AddError(
                    $"Consumable item '{item.DisplayName}' (ID {item.ItemId}) has EffectMagnitude={data.EffectMagnitude}; EffectMagnitude must be > 0.");
            }
        }
    }
}
