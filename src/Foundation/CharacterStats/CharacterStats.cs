using System;
using System.Collections.Generic;
using UnityEngine;

namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Authoritative data store for every numeric attribute that defines a character's
    /// combat capability and survivability.
    /// </summary>
    /// <remarks>
    /// <para><b>Lifecycle:</b> Plain C# class — do NOT inherit from MonoBehaviour or
    /// ScriptableObject. Allocated once per entity at spawn; distributed via dependency
    /// injection. No singletons.</para>
    ///
    /// <para><b>Storage model:</b> Each <see cref="EntityID"/> maps to an <see cref="int"/>
    /// array indexed by <c>(int)<see cref="StatID"/></c> for int-schema stats, and to a
    /// <see cref="float"/> array (indexed via <see cref="StatSchema.FloatStatIndex"/>) for
    /// float-schema stats. Arrays are zeroed on creation. Player-only stats
    /// (Strength, Dexterity, Vitality, Intelligence, MaxMP, CurrentMP, Experience) return
    /// 0 for mob entities because no system ever writes those fields for mobs.</para>
    ///
    /// <para><b>Write contract:</b> <see cref="SetBaseStat"/> and
    /// <see cref="SetBaseStatFloat"/> do NOT enforce StatMin or StatMax. Callers (Leveling
    /// System) are responsible for clamping before writing. The stored value is returned
    /// exactly as written by <see cref="GetBaseStat"/> / <see cref="GetBaseStatFloat"/>.
    /// StatMin and StatMax are enforced only by <see cref="GetEffectiveStat"/> and
    /// <see cref="GetEffectiveStatFloat"/>.</para>
    ///
    /// <para><b>Modifier storage (Story 003):</b> Modifiers are keyed per entity-stat pair.
    /// Each entity maps to a jagged array indexed by <c>(int)StatID</c> — no inner
    /// <c>Dictionary&lt;StatID, T&gt;</c>. This eliminates IL2CPP boxing: <c>StatID : byte</c>
    /// does not implement <c>IEquatable&lt;StatID&gt;</c>, so <c>Dictionary&lt;StatID,T&gt;</c>
    /// would box on every lookup under IL2CPP AOT. Fixed-size per-stat slot arrays remove
    /// that cost entirely.</para>
    /// </remarks>
    public sealed class CharacterStats
    {
        // Int-schema stat array size = index of the highest INT-SCHEMA StatID value + 1.
        // INVARIANT: Experience must remain the highest-valued INT-SCHEMA StatID member.
        // Float-schema stats (CritChance–MovementSpeed) use FloatStatValues; their enum
        // values must never be used as indices into this int array.
        // Falling out of sync produces IndexOutOfRangeException on first access — loud and test-catchable.
        private const int StatArraySize = (int)StatID.Experience + 1;

        // StatSlotCount spans ALL StatID values (int- and float-schema combined).
        // INVARIANT: MovementSpeed must remain the highest-valued StatID member.
        // internal so CharacterStatsFixture can allocate matching slot arrays without hardcoding 17.
        internal const int StatSlotCount = (int)StatID.MovementSpeed + 1; // 17

        private const int EquipmentModifierCapacity = 16;
        private const int BuffModifierCapacity       = 32;

        // One int[] per EntityID, indexed by (int)StatID for int-schema stats. Zeroed on allocation.
        private readonly Dictionary<EntityID, int[]> _statValues = new Dictionary<EntityID, int[]>();

        // -----------------------------------------------------------------------
        // Modifier storage — per entity-stat pair (Story 003).
        //
        // Layout: EntityEquipModifiers[entityId][(int)statId] → EquipmentModifierEntry[16] (or null)
        //         EntityEquipCount[entityId][(int)statId]     → active entry count (int, zero-init)
        //         EntityBuffModifiers[entityId][(int)statId]  → BuffModifierEntry[32] (or null)
        //         EntityBuffCount[entityId][(int)statId]      → active entry count (int, zero-init)
        //
        // Inner indexing uses (int)statId directly into a StatSlotCount-sized jagged array.
        // This eliminates IL2CPP boxing: StatID : byte does not implement IEquatable<StatID>,
        // so Dictionary<StatID,T> would box on every TryGetValue call under IL2CPP AOT.
        // Fixed-size arrays remove the inner dictionary entirely.
        //
        // The outer per-entity slot array (size StatSlotCount) is allocated lazily on first Add.
        // The per-stat modifier array is allocated lazily on first Add for that stat and
        // reused for the entity's lifetime. No heap allocations on the Add/Remove or
        // GetEffectiveStat hot paths after the first call per entity-stat pair.
        //
        // internal: test fixtures (CharacterStatsFixture) seed state directly
        // without going through the public lifecycle API.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Per-entity equipment modifier arrays. Inner jagged array indexed by <c>(int)StatID</c>
        /// (size = <see cref="StatSlotCount"/>); each slot holds a fixed-capacity
        /// <see cref="EquipmentModifierEntry"/> array, or null if no modifier has been added yet.
        /// <c>internal</c> so test fixtures can seed state directly.
        /// </summary>
        internal readonly Dictionary<EntityID, EquipmentModifierEntry[][]> EntityEquipModifiers
            = new Dictionary<EntityID, EquipmentModifierEntry[][]>();

        /// <summary>
        /// Active entry count per stat slot for equipment modifiers.
        /// Indexed by <c>(int)StatID</c>; zero-initialized via <c>new int[StatSlotCount]</c>.
        /// </summary>
        internal readonly Dictionary<EntityID, int[]> EntityEquipCount
            = new Dictionary<EntityID, int[]>();

        /// <summary>
        /// Per-entity buff modifier arrays. Inner jagged array indexed by <c>(int)StatID</c>
        /// (size = <see cref="StatSlotCount"/>); each slot holds a fixed-capacity
        /// <see cref="BuffModifierEntry"/> array, or null if no modifier has been added yet.
        /// <c>internal</c> so test fixtures can seed state directly.
        /// </summary>
        internal readonly Dictionary<EntityID, BuffModifierEntry[][]> EntityBuffModifiers
            = new Dictionary<EntityID, BuffModifierEntry[][]>();

        /// <summary>
        /// Active entry count per stat slot for buff modifiers.
        /// Indexed by <c>(int)StatID</c>; zero-initialized via <c>new int[StatSlotCount]</c>.
        /// </summary>
        internal readonly Dictionary<EntityID, int[]> EntityBuffCount
            = new Dictionary<EntityID, int[]>();

        // -----------------------------------------------------------------------
        // Float-schema stat storage (Story 002).
        // -----------------------------------------------------------------------

        /// <summary>
        /// Per-entity float stat arrays. Indexed by <see cref="StatSchema.FloatStatIndex"/>.
        /// <c>internal</c> so Story 002 test fixtures can seed float base stats directly.
        /// External code uses <see cref="GetBaseStatFloat"/> / <see cref="SetBaseStatFloat"/>.
        /// </summary>
        internal readonly Dictionary<EntityID, float[]> FloatStatValues
            = new Dictionary<EntityID, float[]>();

        // -----------------------------------------------------------------------
        // Story 004: Resource pool storage.
        // CurrentHP and CurrentMP are float fields with exclusive write paths.
        // Never stored in _statValues (int-schema) or FloatStatValues (float modifier stats).
        // -----------------------------------------------------------------------

        private readonly Dictionary<EntityID, float> _currentHp = new Dictionary<EntityID, float>();
        private readonly Dictionary<EntityID, float> _currentMp = new Dictionary<EntityID, float>();

        // Story 005 declares:
        //   public event StatChangedHandler OnStatChanged;
        //   public event EntityDiedHandler  OnEntityDied;

        private readonly ILevelingService _levelingService;

        // -----------------------------------------------------------------------
        // Story 007: Transaction API
        // -----------------------------------------------------------------------
        private const int TransactionDedupCapacity = 18;
        private readonly StatID[]   _deferredStatIds   = new StatID[TransactionDedupCapacity];
        private readonly EntityID[] _deferredEntityIds = new EntityID[TransactionDedupCapacity];
        private int  _deferredCount;
        private bool _transactionOpen;

        /// <summary>
        /// Initializes a new <see cref="CharacterStats"/> container.
        /// </summary>
        /// <param name="levelingService">
        /// Service that owns level-up threshold logic. Injected at construction;
        /// CharacterStats never hardcodes XP thresholds (ADR-010 Tier 1).
        /// </param>
        public CharacterStats(ILevelingService levelingService)
        {
            _levelingService = levelingService ?? throw new ArgumentNullException(nameof(levelingService));
        }

        // -----------------------------------------------------------------------
        // Story 001: Base stat read / write (int-schema)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Retrieves the stored base stat value for the specified entity.
        /// Returns 0 if the entity is unknown or the stat has never been set.
        /// </summary>
        /// <remarks>
        /// Player-only stats (Strength, Dexterity, Vitality, Intelligence, MaxMP,
        /// CurrentMP, Experience) return 0 for mob entities because no system writes
        /// those fields for mobs.
        /// </remarks>
        public int GetBaseStat(EntityID entityId, StatID statId)
        {
            if (!_statValues.TryGetValue(entityId, out int[] statArray))
                return 0;

            return statArray[(int)statId];
        }

        /// <summary>
        /// Stores the base stat value exactly as written.
        /// Does NOT enforce StatMin or StatMax — the caller (Leveling System) is
        /// responsible for clamping before writing. No formula re-evaluation occurs.
        /// </summary>
        public void SetBaseStat(EntityID entityId, StatID statId, int value)
        {
            if (IsFiringAndAssert("SetBaseStat")) return;

            if (!_statValues.TryGetValue(entityId, out int[] statArray))
            {
                statArray = new int[StatArraySize];
                _statValues[entityId] = statArray;
            }

            // TODO: OQ-1 — enforce caller identity for Level write
            statArray[(int)statId] = value;
            if (_transactionOpen)
                AddToDeferredDedup(entityId, statId);
            else
                FireOnStatChanged(entityId, statId);
        }

        // -----------------------------------------------------------------------
        // Story 002: Base stat read / write (float-schema)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Retrieves the base value of a float-schema stat
        /// (CritChance, CritMultiplier, AttackRange, AttackSpeedMultiplier, MovementSpeed).
        /// Returns 0f if the entity is unknown or the stat has never been set.
        /// </summary>
        public float GetBaseStatFloat(EntityID entityId, StatID statId)
        {
            if (!FloatStatValues.TryGetValue(entityId, out float[] arr))
                return 0f;

            return arr[StatSchema.FloatStatIndex(statId)];
        }

        /// <summary>
        /// Stores the base value of a float-schema stat exactly as written.
        /// Does NOT enforce StatMin or StatMax — the caller is responsible for clamping
        /// before writing. No formula re-evaluation occurs. Fires
        /// <see cref="OnStatChanged"/> for <paramref name="statId"/> — deferred and deduped
        /// the same way as <see cref="SetBaseStat"/>/<see cref="SetCurrentHP"/>/
        /// <see cref="SetCurrentMP"/> when a transaction is open (Leveling System Story 006
        /// respec fix), firing immediately otherwise.
        /// </summary>
        public void SetBaseStatFloat(EntityID entityId, StatID statId, float value)
        {
            if (IsFiringAndAssert("SetBaseStatFloat")) return;

            if (!FloatStatValues.TryGetValue(entityId, out float[] arr))
            {
                arr = new float[StatSchema.FloatStatArraySize];
                FloatStatValues[entityId] = arr;
            }

            arr[StatSchema.FloatStatIndex(statId)] = value;
            if (_transactionOpen)
                AddToDeferredDedup(entityId, statId);
            else
                FireOnStatChanged(entityId, statId);
        }

        // -----------------------------------------------------------------------
        // Story 002: Modifier stack (GetEffectiveStat, GetEffectiveStatFloat)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the effective value of an int-schema stat after the full F-1 modifier
        /// stack and StatMin/StatMax clamp:
        /// <c>floor( clamp( (Base + ΣFlatEquip + ΣFlatBuff) × (1 + ΣPctEquip) × (1 + ΣPctBuff), StatMin, StatMax ) )</c>
        /// </summary>
        /// <remarks>
        /// <para>Intra-layer pct bonuses are additive: two +10% equipment bonuses produce
        /// ΣPctEquip = 0.20, not 1.10 × 1.10.</para>
        /// <para>Inter-layer multiplication is separate: the equipment pct layer and the buff
        /// pct layer are two distinct multipliers applied sequentially.</para>
        /// <para>Absent-stat rule: when base stat is 0 and no modifiers are registered for
        /// this entity-stat pair, returns 0 without applying StatMin. This ensures player-only
        /// stats (Strength, Dexterity, Vitality, Intelligence) return 0 for mob entities
        /// exactly as GetBaseStat does (AC-21).</para>
        /// <para>FloorToInt uses <see cref="Mathf.FloorToInt(float)"/> — never
        /// <c>(int)Math.Floor()</c>, which operates in double precision and diverges from
        /// Unity's float domain.</para>
        /// <para>Zero allocation: modifier slot arrays are pre-allocated per entity-stat pair.
        /// This method performs no heap allocations on the hot path.</para>
        /// </remarks>
        public int GetEffectiveStat(EntityID entityId, StatID statId)
        {
            if (StatSchema.IsFloatStat(statId))
                throw new ArgumentException($"{statId} is float-schema — use GetEffectiveStatFloat.", nameof(statId));

            // Resource pool early-exit: CurrentHP and CurrentMP bypass the modifier stack entirely.
            // Written exclusively via ApplyDamage / ApplyRegen / ConsumeMana / ApplyManaRegen.
            if (statId == StatID.CurrentHP)
                return Mathf.FloorToInt(GetCurrentHP(entityId));
            if (statId == StatID.CurrentMP)
                return Mathf.FloorToInt(GetCurrentMP(entityId));

            // Gather modifier arrays and counts for this entity-stat pair.
            EquipmentModifierEntry[] equipArr = null;
            BuffModifierEntry[]      buffArr  = null;
            int equipCount = 0;
            int buffCount  = 0;

            int statIndex = (int)statId;

            if (EntityEquipModifiers.TryGetValue(entityId, out var equipSlots))
                equipArr = equipSlots[statIndex];
            if (EntityEquipCount.TryGetValue(entityId, out var equipCounts))
                equipCount = equipCounts[statIndex];

            if (EntityBuffModifiers.TryGetValue(entityId, out var buffSlots))
                buffArr = buffSlots[statIndex];
            if (EntityBuffCount.TryGetValue(entityId, out var buffCounts))
                buffCount = buffCounts[statIndex];

            float baseStat = GetBaseStat(entityId, statId);

            // Absent-stat guard: a stat that has never been set AND has no modifiers for
            // this entity-stat pair is treated as absent and returns 0 — consistent with
            // GetBaseStat. When any modifier is present, the full formula (including StatMin)
            // applies so a debuff cannot bypass the StatMin floor.
            if (baseStat == 0 && equipCount == 0 && buffCount == 0)
                return 0;

            float flatEquip = 0f;
            float pctEquip  = 0f;
            float flatBuff  = 0f;
            float pctBuff   = 0f;

            for (int i = 0; i < equipCount; i++)
            {
                flatEquip += equipArr[i].FlatBonus;
                pctEquip  += equipArr[i].PctBonus;
            }

            for (int i = 0; i < buffCount; i++)
            {
                flatBuff += buffArr[i].FlatBonus;
                pctBuff  += buffArr[i].PctBonus;
            }

            float effective = (baseStat + flatEquip + flatBuff) * (1f + pctEquip) * (1f + pctBuff);
            float min       = StatSchema.GetStatMin(statId);
            float max       = StatSchema.GetStatMax(statId);

            if (effective < min) effective = min;
            if (effective > max) effective = max;

            return Mathf.FloorToInt(effective);
        }

        /// <summary>
        /// Returns the effective value of a float-schema stat after the full F-1 modifier
        /// stack and StatMin/StatMax clamp. Returns the clamped float directly — no FloorToInt.
        /// </summary>
        /// <remarks>
        /// Applies the same absent-stat rule as <see cref="GetEffectiveStat"/>: when base
        /// stat is 0f and no modifiers are registered for this entity-stat pair, returns 0f
        /// without applying StatMin.
        /// </remarks>
        public float GetEffectiveStatFloat(EntityID entityId, StatID statId)
        {
            if (!StatSchema.IsFloatStat(statId))
                throw new ArgumentException($"{statId} is int-schema — use GetEffectiveStat.", nameof(statId));

            EquipmentModifierEntry[] equipArr = null;
            BuffModifierEntry[]      buffArr  = null;
            int equipCount = 0;
            int buffCount  = 0;

            int statIndex = (int)statId;

            if (EntityEquipModifiers.TryGetValue(entityId, out var equipSlots))
                equipArr = equipSlots[statIndex];
            if (EntityEquipCount.TryGetValue(entityId, out var equipCounts))
                equipCount = equipCounts[statIndex];

            if (EntityBuffModifiers.TryGetValue(entityId, out var buffSlots))
                buffArr = buffSlots[statIndex];
            if (EntityBuffCount.TryGetValue(entityId, out var buffCounts))
                buffCount = buffCounts[statIndex];

            float baseStat = GetBaseStatFloat(entityId, statId);

            if (baseStat == 0f && equipCount == 0 && buffCount == 0)
                return 0f;

            float flatEquip = 0f;
            float pctEquip  = 0f;
            float flatBuff  = 0f;
            float pctBuff   = 0f;

            for (int i = 0; i < equipCount; i++)
            {
                flatEquip += equipArr[i].FlatBonus;
                pctEquip  += equipArr[i].PctBonus;
            }

            for (int i = 0; i < buffCount; i++)
            {
                flatBuff += buffArr[i].FlatBonus;
                pctBuff  += buffArr[i].PctBonus;
            }

            float effective = (baseStat + flatEquip + flatBuff) * (1f + pctEquip) * (1f + pctBuff);
            float min       = StatSchema.GetStatMin(statId);
            float max       = StatSchema.GetStatMax(statId);

            if (effective < min) effective = min;
            if (effective > max) effective = max;

            return effective;
        }

        // -----------------------------------------------------------------------
        // Story 003: Modifier lifecycle — write-lock guard
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns true when <paramref name="statId"/> is a write-locked stat.
        /// Write-locked stats are managed by dedicated systems (HP pools, leveling)
        /// and must never be targeted by external modifier APIs.
        /// </summary>
        private static bool IsWriteLockedStat(StatID statId)
            => statId == StatID.CurrentHP
            || statId == StatID.CurrentMP
            || statId == StatID.Level
            || statId == StatID.Experience;

        // -----------------------------------------------------------------------
        // Story 003: AddBuffModifier / RemoveBuffModifier
        // -----------------------------------------------------------------------

        /// <summary>
        /// Adds a buff modifier to the entity's buff layer for the specified stat.
        /// If a modifier with the same <see cref="BuffModifierEntry.Id"/> already exists,
        /// the existing entry is overwritten (values and duration refreshed) — no double-stack.
        /// </summary>
        /// <param name="entityId">Target entity.</param>
        /// <param name="statId">Stat to modify. Write-locked stats (CurrentHP, CurrentMP, Level, Experience) are rejected.</param>
        /// <param name="entry">Buff modifier entry to add or refresh.</param>
        /// <remarks>
        /// Capacity is 32 entries per entity-stat pair. On overflow, logs an error and
        /// returns without adding — no exception is thrown.
        /// </remarks>
        public void AddBuffModifier(EntityID entityId, StatID statId, BuffModifierEntry entry)
        {
            if (IsFiringAndAssert("AddBuffModifier")) return;

            if (IsWriteLockedStat(statId))
            {
                Debug.LogError($"[CharacterStats] AddBuffModifier: {statId} is write-locked — modifiers cannot target this stat.");
                return;
            }

            if (!EntityBuffModifiers.TryGetValue(entityId, out var buffSlots))
            {
                buffSlots = new BuffModifierEntry[StatSlotCount][];
                EntityBuffModifiers[entityId] = buffSlots;
            }

            int statIndex = (int)statId;
            if (buffSlots[statIndex] == null)
                buffSlots[statIndex] = new BuffModifierEntry[BuffModifierCapacity];
            BuffModifierEntry[] arr = buffSlots[statIndex];

            if (!EntityBuffCount.TryGetValue(entityId, out var buffCounts))
            {
                buffCounts = new int[StatSlotCount];
                EntityBuffCount[entityId] = buffCounts;
            }

            int count = buffCounts[statIndex];

            // Duplicate BuffID → overwrite values and refresh duration; no second entry.
            for (int i = 0; i < count; i++)
            {
                if (arr[i].Id == entry.Id)
                {
                    arr[i] = entry;
                    FireOnStatChanged(entityId, statId);
                    return;
                }
            }

            if (count >= BuffModifierCapacity)
            {
                Debug.LogError($"[CharacterStats] AddBuffModifier: {entityId} stat {statId} buff layer is at capacity ({BuffModifierCapacity}). Modifier not added.");
                return;
            }

            arr[count] = entry;
            buffCounts[statIndex] = count + 1;
            FireOnStatChanged(entityId, statId);
        }

        /// <summary>
        /// Removes the buff modifier identified by <paramref name="buffId"/> from the
        /// entity's buff layer for the specified stat.
        /// Idempotent: if the ID is not found, returns without modification or exception.
        /// </summary>
        /// <param name="entityId">Target entity.</param>
        /// <param name="statId">Stat whose buff layer is searched.</param>
        /// <param name="buffId">Buff ID to remove.</param>
        /// <remarks>
        /// Uses swap-erase to remove in O(1): the found entry is replaced by the last
        /// active entry and the count is decremented. Array order is not preserved.
        /// <see cref="GetEffectiveStat"/> recomputes F-1 from scratch on every call —
        /// no undo-delta is stored.
        /// </remarks>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            if (IsFiringAndAssert("RemoveBuffModifier")) return;

            if (!EntityBuffModifiers.TryGetValue(entityId, out var buffSlots))
                return;
            int statIndex = (int)statId;
            BuffModifierEntry[] arr = buffSlots[statIndex];
            if (arr == null)
                return;
            if (!EntityBuffCount.TryGetValue(entityId, out var buffCounts))
                return;
            int count = buffCounts[statIndex];

            for (int i = 0; i < count; i++)
            {
                if (arr[i].Id == buffId)
                {
                    arr[i]         = arr[count - 1];
                    arr[count - 1] = default;
                    buffCounts[statIndex] = count - 1;
                    FireOnStatChanged(entityId, statId);
                    return;
                }
            }
            // Not found — no-op, no exception.
        }

        // -----------------------------------------------------------------------
        // Story 003: AddEquipmentModifier / RemoveEquipmentModifier
        // -----------------------------------------------------------------------

        /// <summary>
        /// Adds an equipment modifier to the entity's equipment layer for the specified stat.
        /// If a modifier with the same <see cref="EquipmentModifierEntry.Id"/> already exists,
        /// the existing entry is overwritten — no double-counting.
        /// </summary>
        /// <param name="entityId">Target entity.</param>
        /// <param name="statId">Stat to modify. Write-locked stats (CurrentHP, CurrentMP, Level, Experience) are rejected.</param>
        /// <param name="entry">Equipment modifier entry to add or replace.</param>
        /// <remarks>
        /// Capacity is 16 entries per entity-stat pair. On overflow, logs an error and
        /// returns without adding — no exception is thrown.
        /// </remarks>
        public void AddEquipmentModifier(EntityID entityId, StatID statId, EquipmentModifierEntry entry)
        {
            if (IsFiringAndAssert("AddEquipmentModifier")) return;

            if (IsWriteLockedStat(statId))
            {
                Debug.LogError($"[CharacterStats] AddEquipmentModifier: {statId} is write-locked — modifiers cannot target this stat.");
                return;
            }

            if (!EntityEquipModifiers.TryGetValue(entityId, out var equipSlots))
            {
                equipSlots = new EquipmentModifierEntry[StatSlotCount][];
                EntityEquipModifiers[entityId] = equipSlots;
            }

            int statIndex = (int)statId;
            if (equipSlots[statIndex] == null)
                equipSlots[statIndex] = new EquipmentModifierEntry[EquipmentModifierCapacity];
            EquipmentModifierEntry[] arr = equipSlots[statIndex];

            if (!EntityEquipCount.TryGetValue(entityId, out var equipCounts))
            {
                equipCounts = new int[StatSlotCount];
                EntityEquipCount[entityId] = equipCounts;
            }

            int count = equipCounts[statIndex];

            // Duplicate ItemID → overwrite with new flat/pct values; no double-counting.
            for (int i = 0; i < count; i++)
            {
                if (arr[i].Id == entry.Id)
                {
                    arr[i] = entry;
                    FireOnStatChanged(entityId, statId);
                    return;
                }
            }

            if (count >= EquipmentModifierCapacity)
            {
                Debug.LogError($"[CharacterStats] AddEquipmentModifier: {entityId} stat {statId} equipment layer is at capacity ({EquipmentModifierCapacity}). Modifier not added.");
                return;
            }

            arr[count] = entry;
            equipCounts[statIndex] = count + 1;
            FireOnStatChanged(entityId, statId);
        }

        /// <summary>
        /// Removes the equipment modifier identified by <paramref name="itemId"/> from the
        /// entity's equipment layer for the specified stat.
        /// Idempotent: if the ID is not found, returns without modification or exception.
        /// </summary>
        /// <param name="entityId">Target entity.</param>
        /// <param name="statId">Stat whose equipment layer is searched.</param>
        /// <param name="itemId">Item ID to remove.</param>
        /// <remarks>
        /// Both flat and pct bonuses are removed atomically — there is no partial removal.
        /// Uses swap-erase for O(1) removal; array order is not preserved.
        /// </remarks>
        public void RemoveEquipmentModifier(EntityID entityId, StatID statId, ItemID itemId)
        {
            if (IsFiringAndAssert("RemoveEquipmentModifier")) return;

            if (!EntityEquipModifiers.TryGetValue(entityId, out var equipSlots))
                return;
            int statIndex = (int)statId;
            EquipmentModifierEntry[] arr = equipSlots[statIndex];
            if (arr == null)
                return;
            if (!EntityEquipCount.TryGetValue(entityId, out var equipCounts))
                return;
            int count = equipCounts[statIndex];

            for (int i = 0; i < count; i++)
            {
                if (arr[i].Id == itemId)
                {
                    arr[i]         = arr[count - 1];
                    arr[count - 1] = default;
                    equipCounts[statIndex] = count - 1;

                    FireOnStatChanged(entityId, statId);

                    // If MaxHP was reduced, clamp CurrentHP synchronously in the same call (AC-07).
                    if (statId == StatID.MaxHP && _currentHp.TryGetValue(entityId, out float hp))
                    {
                        float newMaxHp = GetEffectiveStat(entityId, StatID.MaxHP);
                        if (hp > newMaxHp)
                        {
                            _currentHp[entityId] = newMaxHp;
                            if (newMaxHp == 0f)
                                FireOnEntityDied(entityId);
                        }
                    }
                    return;
                }
            }
            // Not found — no-op, no exception.
        }

        // -----------------------------------------------------------------------
        // Story 004: Resource pool operations
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns the current HP of the entity as a float.
        /// Returns 0f when the entity has no HP record (never received damage or regen).
        /// </summary>
        public float GetCurrentHP(EntityID entityId) =>
            _currentHp.TryGetValue(entityId, out float v) ? v : 0f;

        /// <summary>
        /// Returns the current MP of the entity as a float.
        /// Returns 0f when the entity has no MP record.
        /// </summary>
        public float GetCurrentMP(EntityID entityId) =>
            _currentMp.TryGetValue(entityId, out float v) ? v : 0f;

        /// <summary>
        /// Reduces CurrentHP by <paramref name="amount"/>, clamped to [0, MaxHP].
        /// Fires <see cref="OnEntityDied"/> exactly once when CurrentHP crosses 0.
        /// No-op when CurrentHP is already 0 — the event does not re-fire on a dead entity.
        /// </summary>
        /// <remarks>
        /// Overkill damage is clamped: an <paramref name="amount"/> larger than CurrentHP
        /// reduces CurrentHP to exactly 0 — no negative HP is stored.
        /// </remarks>
        public void ApplyDamage(EntityID entityId, float amount)
        {
            if (IsFiringAndAssert("ApplyDamage")) return;

            float hp = GetCurrentHP(entityId);
            if (hp == 0f)
                return;

            hp -= amount;
            float maxHp = GetEffectiveStat(entityId, StatID.MaxHP);
            if (hp < 0f) hp = 0f;
            if (hp > maxHp) hp = maxHp;
            _currentHp[entityId] = hp;

            if (hp == 0f)
                FireOnEntityDied(entityId);
        }

        /// <summary>
        /// Restores CurrentHP by <paramref name="amount"/>, clamped to [0, MaxHP].
        /// Does not fire any event. Float precision is preserved — no per-tick rounding.
        /// </summary>
        /// <remarks>
        /// Use only IEEE 754 exactly-representable values in tests
        /// (e.g. 50.25f + 0.75f = 51.0f exactly; 50.3f + 0.7f is imprecise on ARM IL2CPP).
        /// </remarks>
        public void ApplyRegen(EntityID entityId, float amount)
        {
            if (IsFiringAndAssert("ApplyRegen")) return;
            if (amount <= 0f) return;
            float hp = GetCurrentHP(entityId);
            hp += amount;
            float maxHp = GetEffectiveStat(entityId, StatID.MaxHP);
            if (hp < 0f) hp = 0f;
            if (hp > maxHp) hp = maxHp;
            _currentHp[entityId] = hp;
        }

        /// <summary>
        /// Attempts to deduct <paramref name="cost"/> from CurrentMP.
        /// Returns <see langword="true"/> and writes the new value when CurrentMP &gt;= cost.
        /// Returns <see langword="false"/> and makes no write when MP is insufficient.
        /// </summary>
        public bool ConsumeMana(EntityID entityId, float cost)
        {
            if (IsFiringAndAssert("ConsumeMana")) return false;

            float mp = GetCurrentMP(entityId);
            if (mp < cost)
                return false;

            mp -= cost;
            float maxMp = GetEffectiveStat(entityId, StatID.MaxMP);
            if (mp < 0f) mp = 0f;
            if (mp > maxMp) mp = maxMp;
            _currentMp[entityId] = mp;
            return true;
        }

        /// <summary>
        /// Restores CurrentMP by <paramref name="amount"/>, clamped to [0, MaxMP].
        /// Does not fire any event.
        /// </summary>
        public void ApplyManaRegen(EntityID entityId, float amount)
        {
            if (IsFiringAndAssert("ApplyManaRegen")) return;
            if (amount <= 0f) return;
            float mp = GetCurrentMP(entityId);
            mp += amount;
            float maxMp = GetEffectiveStat(entityId, StatID.MaxMP);
            if (mp < 0f) mp = 0f;
            if (mp > maxMp) mp = maxMp;
            _currentMp[entityId] = mp;
        }

        // -----------------------------------------------------------------------
        // Leveling System Story 002: discrete full-state resource-pool overwrite.
        // Unlike ApplyRegen/ApplyDamage (deltas), these are direct writes — used by
        // CR-2.7's level-up HP/MP restore, which is a full-state reset, not a regen tick.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Forcibly overwrites CurrentHP to <paramref name="value"/>, clamped to
        /// <c>[0, GetEffectiveStat(MaxHP)]</c> — the modifier-inclusive ceiling, matching
        /// <see cref="ApplyDamage"/>/<see cref="ApplyRegen"/>'s existing clamp convention.
        /// Fires <see cref="OnStatChanged"/> for <see cref="StatID.CurrentHP"/> — deferred and
        /// deduped the same way as <see cref="SetBaseStat"/> when a transaction is open
        /// (Leveling System Story 006 respec fix), firing immediately otherwise.
        /// </summary>
        /// <remarks>
        /// Callers do not need to compute the correct ceiling themselves — pass the
        /// freshly-written target value (or an intentionally large value to mean "fill to
        /// max") and this method clamps against the true effective ceiling internally.
        /// Added for the Leveling System's CR-2.7 level-up HP restore (Story 002): a
        /// level-up is a discrete full-state reset, not a regen delta, so it fires an event
        /// (unlike <see cref="ApplyRegen"/>) and does not accumulate against the prior value.
        /// </remarks>
        public void SetCurrentHP(EntityID entityId, float value)
        {
            if (IsFiringAndAssert("SetCurrentHP")) return;

            float maxHp = GetEffectiveStat(entityId, StatID.MaxHP);
            if (value < 0f) value = 0f;
            if (value > maxHp) value = maxHp;
            _currentHp[entityId] = value;
            if (_transactionOpen)
                AddToDeferredDedup(entityId, StatID.CurrentHP);
            else
                FireOnStatChanged(entityId, StatID.CurrentHP);
        }

        /// <summary>
        /// Forcibly overwrites CurrentMP to <paramref name="value"/>, clamped to
        /// <c>[0, GetEffectiveStat(MaxMP)]</c>. Fires <see cref="OnStatChanged"/> for
        /// <see cref="StatID.CurrentMP"/>. Same discrete-overwrite contract as
        /// <see cref="SetCurrentHP"/> — see its remarks, including transaction-aware
        /// deferral/dedup when a transaction is open (Leveling System Story 006 respec fix).
        /// </summary>
        public void SetCurrentMP(EntityID entityId, float value)
        {
            if (IsFiringAndAssert("SetCurrentMP")) return;

            float maxMp = GetEffectiveStat(entityId, StatID.MaxMP);
            if (value < 0f) value = 0f;
            if (value > maxMp) value = maxMp;
            _currentMp[entityId] = value;
            if (_transactionOpen)
                AddToDeferredDedup(entityId, StatID.CurrentMP);
            else
                FireOnStatChanged(entityId, StatID.CurrentMP);
        }

        // -----------------------------------------------------------------------
        // Story 005: Events — OnStatChanged / OnEntityDied
        // Named delegate types per ADR-010 (prevents IL2CPP boxing for two value-type params).
        // Fixed-capacity subscriber arrays iterated by index — no List<T>/foreach.
        // Single _isFiring guard covers both events; write operations throw in dev builds
        // when called from inside a handler. Read-only queries are always permitted.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Handler for stat-changed notifications. Named delegate (not raw Action&lt;T,U&gt;) to
        /// prevent IL2CPP boxing when two value-type parameters are passed on the hot path.
        /// </summary>
        public delegate void StatChangedHandler(EntityID entityId, StatID statId);

        /// <summary>Handler for entity-death notifications.</summary>
        public delegate void EntityDiedHandler(EntityID entityId);

        private const int EventSubscriberCapacity = 16;

        private readonly StatChangedHandler[] _statChangedSubscribers = new StatChangedHandler[EventSubscriberCapacity];
        private int _statChangedCount;

        private readonly EntityDiedHandler[] _entityDiedSubscribers = new EntityDiedHandler[EventSubscriberCapacity];
        private int _entityDiedCount;

        // True while subscriber invocation is in progress.
        // Write operations are forbidden during handler execution — see IsFiringAndAssert.
        private bool _isFiring;

        // Returns true when the caller should bail out silently (release-build re-entrance).
        // In UNITY_EDITOR / DEVELOPMENT_BUILD, throws InvalidOperationException instead.
        private bool IsFiringAndAssert(string operation)
        {
            if (!_isFiring) return false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            throw new System.InvalidOperationException(
                $"[CharacterStats] Re-entrance: '{operation}' cannot be called from inside an event handler. " +
                "Read-only queries (GetEffectiveStat, GetBaseStat) are always permitted.");
#else
            return true;
#endif
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to stat-changed notifications (max 16 slots).
        /// On overflow logs an error and returns — raise <c>EventSubscriberCapacity</c> in code.
        /// </summary>
        public void Subscribe(StatChangedHandler handler)
        {
            if (IsFiringAndAssert("Subscribe")) return;
            if (_statChangedCount >= EventSubscriberCapacity)
            {
                UnityEngine.Debug.LogError($"[CharacterStats] OnStatChanged subscriber capacity ({EventSubscriberCapacity}) exceeded. Raise EventSubscriberCapacity.");
                return;
            }
            _statChangedSubscribers[_statChangedCount++] = handler;
        }

        /// <summary>
        /// Removes <paramref name="handler"/> from stat-changed notifications. No-op if not subscribed.
        /// </summary>
        public void Unsubscribe(StatChangedHandler handler)
        {
            if (IsFiringAndAssert("Unsubscribe")) return;
            for (int i = 0; i < _statChangedCount; i++)
            {
                if (_statChangedSubscribers[i] == handler)
                {
                    _statChangedSubscribers[i] = _statChangedSubscribers[_statChangedCount - 1];
                    _statChangedSubscribers[--_statChangedCount] = null;
                    return;
                }
            }
        }

        /// <summary>
        /// Subscribes <paramref name="handler"/> to entity-death notifications (max 16 slots).
        /// On overflow logs an error and returns — raise <c>EventSubscriberCapacity</c> in code.
        /// </summary>
        public void Subscribe(EntityDiedHandler handler)
        {
            if (IsFiringAndAssert("Subscribe")) return;
            if (_entityDiedCount >= EventSubscriberCapacity)
            {
                UnityEngine.Debug.LogError($"[CharacterStats] OnEntityDied subscriber capacity ({EventSubscriberCapacity}) exceeded. Raise EventSubscriberCapacity.");
                return;
            }
            _entityDiedSubscribers[_entityDiedCount++] = handler;
        }

        /// <summary>
        /// Removes <paramref name="handler"/> from entity-death notifications. No-op if not subscribed.
        /// </summary>
        public void Unsubscribe(EntityDiedHandler handler)
        {
            if (IsFiringAndAssert("Unsubscribe")) return;
            for (int i = 0; i < _entityDiedCount; i++)
            {
                if (_entityDiedSubscribers[i] == handler)
                {
                    _entityDiedSubscribers[i] = _entityDiedSubscribers[_entityDiedCount - 1];
                    _entityDiedSubscribers[--_entityDiedCount] = null;
                    return;
                }
            }
        }

        private void FireOnStatChanged(EntityID entityId, StatID statId)
        {
            _isFiring = true;
            try
            {
                int count = _statChangedCount;
                for (int i = 0; i < count; i++)
                    _statChangedSubscribers[i]?.Invoke(entityId, statId);
            }
            finally
            {
                _isFiring = false;
            }
        }

        private void FireOnEntityDied(EntityID entityId)
        {
            _isFiring = true;
            try
            {
                int count = _entityDiedCount;
                for (int i = 0; i < count; i++)
                    _entityDiedSubscribers[i]?.Invoke(entityId);
            }
            finally
            {
                _isFiring = false;
            }
        }

        // -----------------------------------------------------------------------
        // Story 006: Write ownership
        // ILevelingService injection, mob guard, write-locked stat rejection.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Awards <paramref name="amount"/> experience to the entity.
        /// Delegates level-up threshold detection to the injected ILevelingService (ADR-010 Tier 1).
        /// No-op for mob entities or non-positive amounts.
        /// </summary>
        public void AddExperience(EntityID entityId, int amount)
        {
            if (!_levelingService.IsPlayerEntity(entityId)) return;
            if (amount <= 0) return;

            int current = GetBaseStat(entityId, StatID.Experience);
            SetBaseStat(entityId, StatID.Experience, current + amount);

            if (current + amount >= _levelingService.GetExperienceThreshold(entityId))
                _levelingService.NotifyExperienceCrossedThreshold(entityId);
        }

        // -----------------------------------------------------------------------
        // Story 007: Transaction API (implementation)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Opens a stat transaction. Base stat writes (<see cref="SetBaseStat"/>) are applied
        /// immediately; <see cref="StatChangedHandler"/> events are deferred until
        /// <see cref="EndStatTransaction"/> fires them in batch, deduplicated per
        /// (EntityID, StatID) pair.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when a transaction is already open.</exception>
        public void BeginStatTransaction()
        {
            if (IsFiringAndAssert("BeginStatTransaction")) return;
            if (_transactionOpen)
                throw new InvalidOperationException(
                    "[CharacterStats] BeginStatTransaction: a transaction is already open. Transactions are non-nestable.");
            _transactionOpen = true;
        }

        /// <summary>
        /// Closes the open stat transaction. Fires <see cref="StatChangedHandler"/> once per
        /// unique (EntityID, StatID) pair written during the transaction, then clears the
        /// deferred queue.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when no transaction is open.</exception>
        public void EndStatTransaction()
        {
            if (IsFiringAndAssert("EndStatTransaction")) return;
            if (!_transactionOpen)
                throw new InvalidOperationException(
                    "[CharacterStats] EndStatTransaction: no transaction is open.");
            // Close first — if a handler throws, the transaction is not left stuck open.
            _transactionOpen = false;
            int count = _deferredCount;
            _deferredCount = 0;
            for (int i = 0; i < count; i++)
                FireOnStatChanged(_deferredEntityIds[i], _deferredStatIds[i]);
        }

        /// <summary>
        /// Discards the deferred event queue without firing any <see cref="StatChangedHandler"/>
        /// events. Base stat writes made during the transaction are <b>preserved</b> — only the
        /// pending events are discarded. Safe to call when no transaction is open (no-op).
        /// </summary>
        public void RollbackStatTransaction()
        {
            if (IsFiringAndAssert("RollbackStatTransaction")) return;
            if (!_transactionOpen)
                return;
            _deferredCount = 0;
            _transactionOpen = false;
        }

        private void AddToDeferredDedup(EntityID entityId, StatID statId)
        {
            for (int i = 0; i < _deferredCount; i++)
            {
                if (_deferredEntityIds[i] == entityId && _deferredStatIds[i] == statId)
                    return;
            }
            // Overflow is silent in release — capacity of 18 covers all 17 StatIDs + 1 buffer.
            if (_deferredCount < TransactionDedupCapacity)
            {
                _deferredEntityIds[_deferredCount] = entityId;
                _deferredStatIds[_deferredCount]   = statId;
                _deferredCount++;
            }
        }
    }
}
