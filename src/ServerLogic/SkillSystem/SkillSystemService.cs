using System;
using System.Collections.Generic;
using System.Globalization;
using IronGrind.CharacterStats;
using IronGrind.LevelingSystem;
using IronGrind.StatusEffects;
using UnityEngine;

namespace IronGrind.SkillSystem
{
    /// <summary>
    /// Per-entity skill store (skill-system.md CR-SK-20, EC-SK-12, AC-SK-10/13/47/48; Skill System Story 002).
    /// One fixed record per player zone slot (sized from the zone's capacity), allocated once in the constructor;
    /// spawn, despawn and reads do not allocate. Players only (CR-SK-17): a mob index is refused. Reads for an entity
    /// without a record scan every record (fine for spawn-time and test callers; add an index if a per-tick caller
    /// needs it). Level-ups unlock skills and notify the owner through <see cref="ISkillUnlockOutput"/>
    /// (CR-SK-21, Story 003).
    /// </summary>
    public sealed class SkillSystemService : ISkillSystem
    {
        /// <summary>Skill slots per entity (GDD: 10).</summary>
        public const int MAX_SKILLS_PER_ENTITY = 10;

        // Player slots are byte indices (ZoneSlotPool).
        private const int MAX_PLAYERS_LIMIT = 255;

        private const string LOG_PREFIX = "[SkillSystem] ";

        private readonly IClassRegistry _classRegistry;
        private readonly ISkillDefinitionSource _definitions;
        private readonly ISkillLevelSource _levels;
        private readonly IEntitySlotIndex _slotIndex;
        private readonly ISkillUnlockOutput _unlockOutput;
        private ILevelingEventBroadcaster _subscribedTo;

        private readonly SkillInstance[][] _skills;
        private readonly int[] _counts;
        private readonly EntityID[] _owners;
        private readonly bool[] _inUse;

        /// <summary>Creates the service and allocates the whole store, one record per player slot.</summary>
        /// <param name="classRegistry">Class skill lists.</param>
        /// <param name="definitions">Skill definitions (the <see cref="SkillDatabase"/> asset in production).</param>
        /// <param name="levels">Level read at spawn.</param>
        /// <param name="slotIndex">Entity to zone slot index; players map to [0, <paramref name="maxPlayers"/>).</param>
        /// <param name="unlockOutput">Sends the unlock notification to the owner (CR-SK-21).</param>
        /// <param name="maxPlayers">
        /// The zone's player capacity, <c>ZoneSlotPool.MaxPlayers</c>, in [1, 255]. A slot index at or above it is a
        /// mob index (<c>ZoneSlotPoolEntityIndex</c>) and is refused.
        /// </param>
        /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxPlayers"/> is outside [1, 255].</exception>
        public SkillSystemService(
            IClassRegistry classRegistry,
            ISkillDefinitionSource definitions,
            ISkillLevelSource levels,
            IEntitySlotIndex slotIndex,
            ISkillUnlockOutput unlockOutput,
            int maxPlayers)
        {
            _classRegistry = classRegistry ?? throw new ArgumentNullException(nameof(classRegistry));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _levels = levels ?? throw new ArgumentNullException(nameof(levels));
            _slotIndex = slotIndex ?? throw new ArgumentNullException(nameof(slotIndex));
            _unlockOutput = unlockOutput ?? throw new ArgumentNullException(nameof(unlockOutput));
            if (maxPlayers < 1 || maxPlayers > MAX_PLAYERS_LIMIT)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPlayers), maxPlayers, "must be in [1, 255]");
            }

            _skills = new SkillInstance[maxPlayers][];
            for (int i = 0; i < maxPlayers; i++)
            {
                _skills[i] = new SkillInstance[MAX_SKILLS_PER_ENTITY];
            }

            _counts = new int[maxPlayers];
            _owners = new EntityID[maxPlayers];
            _inUse = new bool[maxPlayers];
        }

        /// <inheritdoc />
        public void OnEntitySpawned(EntityID entity, ClassType classType)
        {
            if (!_slotIndex.TryGetSlotIndex(entity, out int record) || record < 0 || record >= _inUse.Length)
            {
                Debug.LogWarning(LOG_PREFIX + "OnEntitySpawned ignored: " + entity
                    + " has no player slot (players only, CR-SK-17)");
                return;
            }

            if (_inUse[record])
            {
                Debug.LogWarning(LOG_PREFIX + "OnEntitySpawned for " + entity + " on a record still held by "
                    + (_owners[record] == entity ? "the same entity" : "a different entity " + _owners[record])
                    + " (spawn without despawn); re-initializing in place");
            }

            ReleaseStaleRecords(entity, record);

            IReadOnlyList<SkillID> classSkills = _classRegistry.GetClassSkills((byte)classType);
            int count = UsableSkillCount(entity, classType, classSkills.Count);
            if (count > 0)
            {
                FillSlots(entity, _skills[record], classSkills, count);
            }

            ClearSlots(_skills[record], count);
            _counts[record] = count;
            _owners[record] = entity;
            _inUse[record] = true;
        }

        // How many class skills the record holds: 0 with a warning for an empty list (EC-SK-12), the first
        // MAX_SKILLS_PER_ENTITY with an error for a longer one.
        private static int UsableSkillCount(EntityID entity, ClassType classType, int count)
        {
            if (count == 0)
            {
                Debug.LogWarning(LOG_PREFIX + "class " + ((byte)classType).ToString(CultureInfo.InvariantCulture)
                    + " has no skills for " + entity + " (EC-SK-12)");
                return 0;
            }

            if (count > MAX_SKILLS_PER_ENTITY)
            {
                Debug.LogError(LOG_PREFIX + "class " + ((byte)classType).ToString(CultureInfo.InvariantCulture)
                    + " lists " + count.ToString(CultureInfo.InvariantCulture) + " skills; only the first "
                    + MAX_SKILLS_PER_ENTITY.ToString(CultureInfo.InvariantCulture) + " are used");
                return MAX_SKILLS_PER_ENTITY;
            }

            return count;
        }

        // The same entity may still hold a stale record under another slot (moved without a despawn): release it,
        // so the owner scan in FindRecord can never return it.
        private void ReleaseStaleRecords(EntityID entity, int record)
        {
            for (int i = 0; i < _inUse.Length; i++)
            {
                if (i != record && _inUse[i] && _owners[i] == entity)
                {
                    Debug.LogWarning(LOG_PREFIX + entity + " still held record "
                        + i.ToString(CultureInfo.InvariantCulture) + " (spawn without despawn); released");
                    ReleaseRecord(i);
                }
            }
        }

        // Writes slots [0, count): id from the class list, unlock from the level read once (CR-SK-20), no cooldown.
        // A missing definition keeps the slot locked with one error (story decision).
        private void FillSlots(EntityID entity, SkillInstance[] slots, IReadOnlyList<SkillID> classSkills, int count)
        {
            int level = _levels.GetLevel(entity);
            if (level < 1)
            {
                Debug.LogWarning(LOG_PREFIX + entity + " spawned at level " + level.ToString(CultureInfo.InvariantCulture)
                    + " (not registered in CharacterStats yet?); every skill stays locked");
            }

            for (int i = 0; i < count; i++)
            {
                SkillID id = classSkills[i];
                bool unlocked = false;
                if (_definitions.TryGetDefinition(id, out SkillDefinition definition))
                {
                    unlocked = level >= definition.UnlockLevel;
                }
                else
                {
                    Debug.LogError(LOG_PREFIX + id + " in the class list has no SkillDatabase definition; slot "
                        + i.ToString(CultureInfo.InvariantCulture) + " stays locked");
                }

                slots[i] = new SkillInstance { SkillID = id, IsUnlocked = unlocked, CooldownExpiryTick = 0 };
            }
        }

        /// <inheritdoc />
        public void OnEntityDespawned(EntityID entity)
        {
            int record = FindRecord(entity);
            if (record < 0)
            {
                return;
            }

            ReleaseRecord(record);
        }

        /// <inheritdoc />
        /// <remarks>
        /// CR-SK-21: every locked slot whose definition has <c>UnlockLevel == newLevel</c> unlocks permanently and
        /// sends one notification to the owner, in slot order. Equality, not <c>&lt;=</c>: the Leveling System fires
        /// once per level gained (CR-2.10), so every level is seen. An entity without a record, and a slot without a
        /// definition (already logged at spawn), are skipped silently. Mutates no stats. Two passes: every matching
        /// slot is unlocked first, then the notices are sent, so a throwing output can never leave a later slot
        /// locked and an output that reads the store sees all unlocks. The service does not catch an output exception.
        /// </remarks>
        public void OnLevelUp(EntityID entity, int newLevel)
        {
            int record = FindRecord(entity);
            if (record < 0)
            {
                return;
            }

            ushort newlyUnlocked = UnlockMatchingSlots(record, newLevel);
            if (newlyUnlocked == 0)
            {
                return;
            }

            SkillInstance[] slots = _skills[record];
            for (int i = 0; i < _counts[record]; i++)
            {
                if ((newlyUnlocked & (1 << i)) != 0)
                {
                    _unlockOutput.SendUnlock(entity, slots[i].SkillID);
                }
            }
        }

        // Unlocks every locked slot whose definition's UnlockLevel equals newLevel; returns a bitmask of those slots
        // (bit i = slot i; MAX_SKILLS_PER_ENTITY = 10 fits a ushort).
        private ushort UnlockMatchingSlots(int record, int newLevel)
        {
            ushort mask = 0;
            SkillInstance[] slots = _skills[record];
            for (int i = 0; i < _counts[record]; i++)
            {
                if (slots[i].IsUnlocked
                    || !_definitions.TryGetDefinition(slots[i].SkillID, out SkillDefinition definition)
                    || definition.UnlockLevel != newLevel)
                {
                    continue;
                }

                slots[i].IsUnlocked = true;
                mask |= (ushort)(1 << i);
            }

            return mask;
        }

        /// <summary>
        /// Subscribes <see cref="HandleLevelUp"/> to <paramref name="broadcaster"/>'s <c>OnLevelUp</c> (ADR-010: a
        /// named method, never a lambda). The composition root calls this once (Story 011). One subscription at a
        /// time: a second call, for any broadcaster, would double every notice, so it throws.
        /// </summary>
        /// <param name="broadcaster">The Leveling System's event source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="broadcaster"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Already subscribed; call <see cref="Unsubscribe"/> first.</exception>
        public void Subscribe(ILevelingEventBroadcaster broadcaster)
        {
            if (broadcaster == null)
            {
                throw new ArgumentNullException(nameof(broadcaster));
            }

            if (_subscribedTo != null)
            {
                throw new InvalidOperationException("SkillSystemService is already subscribed to a level-up broadcaster");
            }

            broadcaster.OnLevelUp += HandleLevelUp;
            _subscribedTo = broadcaster;
        }

        /// <summary>
        /// Removes the subscription made by <see cref="Subscribe"/>. A no-op when <paramref name="broadcaster"/> is not
        /// the one subscribed to (including when nothing is subscribed).
        /// </summary>
        /// <param name="broadcaster">The same event source passed to <see cref="Subscribe"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="broadcaster"/> is null.</exception>
        public void Unsubscribe(ILevelingEventBroadcaster broadcaster)
        {
            if (broadcaster == null)
            {
                throw new ArgumentNullException(nameof(broadcaster));
            }

            if (!ReferenceEquals(_subscribedTo, broadcaster))
            {
                return;
            }

            broadcaster.OnLevelUp -= HandleLevelUp;
            _subscribedTo = null;
        }

        private void HandleLevelUp(LevelUpEventArgs args)
        {
            OnLevelUp(args.EntityId, args.NewLevel);
        }

        /// <summary>Number of skill slots held for the entity; 0 when it has no record.</summary>
        internal int GetSkillCount(EntityID entity)
        {
            int record = FindRecord(entity);
            return record < 0 ? 0 : _counts[record];
        }

        /// <summary>Reads slot <paramref name="slot"/>; false for no record or a slot outside [0, count).</summary>
        internal bool TryGetSkill(EntityID entity, int slot, out SkillInstance skill)
        {
            skill = default;
            int record = FindRecord(entity);
            if (record < 0 || slot < 0 || slot >= _counts[record])
            {
                return false;
            }

            skill = _skills[record][slot];
            return true;
        }

        /// <summary>Finds the slot holding <paramref name="skillId"/>; false when absent.</summary>
        internal bool TryFindSlot(EntityID entity, SkillID skillId, out int slot)
        {
            slot = -1;
            int record = FindRecord(entity);
            if (record < 0)
            {
                return false;
            }

            SkillInstance[] slots = _skills[record];
            for (int i = 0; i < _counts[record]; i++)
            {
                if (slots[i].SkillID == skillId)
                {
                    slot = i;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the current cooldown expiry tick of one of the entity's skills by skill id (Story 009). Scans the
        /// entity's at most <see cref="MAX_SKILLS_PER_ENTITY"/> slots; allocation-free.
        /// </summary>
        /// <param name="entity">The skill owner.</param>
        /// <param name="skillId">The raw skill id.</param>
        /// <param name="cooldownExpiryTick">The expiry tick, or 0 when not found.</param>
        /// <returns>False when the entity has no record or no slot holds the skill.</returns>
        public bool TryGetCooldownExpiry(EntityID entity, uint skillId, out uint cooldownExpiryTick)
        {
            cooldownExpiryTick = 0;
            int record = FindRecord(entity);
            if (record < 0)
            {
                return false;
            }

            SkillInstance[] slots = _skills[record];
            for (int i = 0; i < _counts[record]; i++)
            {
                if (slots[i].SkillID.Value == skillId)
                {
                    cooldownExpiryTick = slots[i].CooldownExpiryTick;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Test seam: writes a cooldown expiry into a slot; false when the slot does not exist.</summary>
        internal bool SetCooldownForTesting(EntityID entity, int slot, uint expiryTick)
        {
            return SetCooldownExpiry(entity, slot, expiryTick);
        }

        /// <summary>
        /// Writes a slot's cooldown expiry (CR-SK-8, Story 006). False when the entity has no record or the slot does
        /// not exist. Allocation-free.
        /// </summary>
        /// <remarks>The executor does not treat false as a rejection: it cannot happen in the tick that validated the slot.</remarks>
        internal bool SetCooldownExpiry(EntityID entity, int slot, uint expiryTick)
        {
            int record = FindRecord(entity);
            if (record < 0 || slot < 0 || slot >= _counts[record])
            {
                return false;
            }

            _skills[record][slot].CooldownExpiryTick = expiryTick;
            return true;
        }

        // Resolves the entity's record: by slot index first, then by an owner scan (the slot may be released
        // already). A record held by another entity is never returned. Returns -1 when none.
        private int FindRecord(EntityID entity)
        {
            if (_slotIndex.TryGetSlotIndex(entity, out int record)
                && record >= 0 && record < _inUse.Length
                && _inUse[record] && _owners[record] == entity)
            {
                return record;
            }

            for (int i = 0; i < _inUse.Length; i++)
            {
                if (_inUse[i] && _owners[i] == entity)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ReleaseRecord(int record)
        {
            ClearSlots(_skills[record], 0);
            _counts[record] = 0;
            _owners[record] = default;
            _inUse[record] = false;
        }

        private static void ClearSlots(SkillInstance[] slots, int from)
        {
            for (int i = from; i < slots.Length; i++)
            {
                slots[i] = default;
            }
        }
    }
}
