using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>Fake <see cref="IBuffModifierLayer"/> that only counts calls.</summary>
    internal sealed class RecordingBuffModifierLayer : IBuffModifierLayer
    {
        /// <summary>Number of TryAddBuffModifier calls.</summary>
        public int AddCalls { get; private set; }

        /// <summary>Number of RemoveBuffModifier calls.</summary>
        public int RemoveCalls { get; private set; }

        /// <summary>Total calls of either kind.</summary>
        public int TotalCalls => AddCalls + RemoveCalls;

        /// <inheritdoc/>
        public bool TryAddBuffModifier(EntityID entityId, StatID statId, BuffModifierEntry entry)
        {
            AddCalls++;
            return true;
        }

        /// <inheritdoc/>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            RemoveCalls++;
        }
    }

    /// <summary>Fake <see cref="IEntitySlotIndex"/> backed by a dictionary.</summary>
    internal sealed class DictionaryEntitySlotIndex : IEntitySlotIndex
    {
        private readonly Dictionary<EntityID, int> _slots = new Dictionary<EntityID, int>();

        /// <summary>Registers an entity at a slot index.</summary>
        public void Add(EntityID id, int index)
        {
            _slots[id] = index;
        }

        /// <inheritdoc/>
        public bool TryGetSlotIndex(EntityID id, out int index)
        {
            return _slots.TryGetValue(id, out index);
        }
    }
}
