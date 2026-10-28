using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>Kind of a recorded layer call.</summary>
    internal enum LayerCallKind
    {
        Add,
        Remove
    }

    /// <summary>One recorded layer call; <see cref="Entry"/> is meaningful for adds only.</summary>
    internal readonly struct LayerCall
    {
        public readonly LayerCallKind Kind;
        public readonly EntityID Entity;
        public readonly StatID Stat;
        public readonly BuffID Buff;
        public readonly BuffModifierEntry Entry;

        public LayerCall(LayerCallKind kind, EntityID entity, StatID stat, BuffID buff, BuffModifierEntry entry)
        {
            Kind = kind;
            Entity = entity;
            Stat = stat;
            Buff = buff;
            Entry = entry;
        }
    }

    /// <summary>
    /// Fake <see cref="IBuffModifierLayer"/> that records an ordered call log and can fail the Nth add.
    /// </summary>
    internal sealed class RecordingBuffModifierLayer : IBuffModifierLayer
    {
        /// <summary>Ordered log of every call.</summary>
        public List<LayerCall> Calls { get; } = new List<LayerCall>();

        /// <summary>1-based index of the add call that returns false; 0 means never fail.</summary>
        public int FailOnAddNumber { get; set; }

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
            Calls.Add(new LayerCall(LayerCallKind.Add, entityId, statId, entry.Id, entry));
            return AddCalls != FailOnAddNumber;
        }

        /// <inheritdoc/>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            RemoveCalls++;
            Calls.Add(new LayerCall(LayerCallKind.Remove, entityId, statId, buffId, default));
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
