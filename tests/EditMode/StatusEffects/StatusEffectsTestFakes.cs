using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.StatusEffects;

namespace IronGrind.Tests.EditMode.StatusEffects
{
    /// <summary>Kind of a recorded layer call.</summary>
    internal enum LayerCallKind
    {
        Add,
        Remove,
        Regen,
        ManaRegen
    }

    /// <summary>One recorded layer call; <see cref="Entry"/> is meaningful for adds only.</summary>
    internal readonly struct LayerCall
    {
        public readonly LayerCallKind Kind;
        public readonly EntityID Entity;
        public readonly StatID Stat;
        public readonly BuffID Buff;
        public readonly BuffModifierEntry Entry;
        /// <summary>Regen amount; meaningful for Regen and ManaRegen calls only.</summary>
        public readonly float Amount;

        public LayerCall(LayerCallKind kind, EntityID entity, StatID stat, BuffID buff, BuffModifierEntry entry, float amount = 0f)
        {
            Amount = amount;
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
    internal sealed class RecordingBuffModifierLayer : IBuffModifierLayer, IStatResourceSink
    {
        private readonly IBuffModifierLayer _innerLayer;
        private readonly IStatResourceSink _innerSink;

        /// <summary>Pure fake: records only.</summary>
        public RecordingBuffModifierLayer()
        {
        }

        /// <summary>Decorator: records, then forwards to the inner layer and sink (real stats change).</summary>
        public RecordingBuffModifierLayer(IBuffModifierLayer innerLayer, IStatResourceSink innerSink)
        {
            _innerLayer = innerLayer;
            _innerSink = innerSink;
        }

        /// <summary>Number of ApplyRegen calls.</summary>
        public int RegenCalls { get; private set; }

        /// <summary>Number of ApplyManaRegen calls.</summary>
        public int ManaRegenCalls { get; private set; }

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
            if (_innerLayer != null)
                return _innerLayer.TryAddBuffModifier(entityId, statId, entry);
            return AddCalls != FailOnAddNumber;
        }

        /// <inheritdoc/>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            RemoveCalls++;
            Calls.Add(new LayerCall(LayerCallKind.Remove, entityId, statId, buffId, default));
            _innerLayer?.RemoveBuffModifier(entityId, statId, buffId);
        }

        /// <inheritdoc/>
        public void ApplyRegen(EntityID entityId, float amount)
        {
            RegenCalls++;
            Calls.Add(new LayerCall(LayerCallKind.Regen, entityId, default, default, default, amount));
            _innerSink?.ApplyRegen(entityId, amount);
        }

        /// <inheritdoc/>
        public void ApplyManaRegen(EntityID entityId, float amount)
        {
            ManaRegenCalls++;
            Calls.Add(new LayerCall(LayerCallKind.ManaRegen, entityId, default, default, default, amount));
            _innerSink?.ApplyManaRegen(entityId, amount);
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
