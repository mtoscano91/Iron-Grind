using System;
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
        ManaRegen,
        BeginBatch,
        EndBatch,
        ClampHp,
        ClampMp
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

        /// <summary>
        /// 1-based index (cumulative across the fake's lifetime, not reset by <c>Calls.Clear()</c>) of the add call
        /// that returns false; 0 means never fail. Applies with or without an inner layer; the failing add is
        /// recorded but not forwarded to the inner layer.
        /// </summary>
        public int FailOnAddNumber { get; set; }

        /// <summary>
        /// 1-based index (cumulative, like <see cref="FailOnAddNumber"/>) of the add call that throws
        /// <see cref="InvalidOperationException"/>; 0 means never throw. The throwing add is recorded but not forwarded.
        /// </summary>
        public int ThrowOnAddNumber { get; set; }

        /// <summary>
        /// 1-based index (cumulative) of the RemoveBuffModifier call that throws <see cref="InvalidOperationException"/>;
        /// 0 means never. The throwing remove is recorded but not forwarded.
        /// </summary>
        public int ThrowOnRemoveNumber { get; set; }

        /// <summary>
        /// 1-based index (cumulative) of the EndStatBatch call that throws <see cref="InvalidOperationException"/>;
        /// 0 means never. The inner layer's End is still forwarded first, so a real transaction is closed.
        /// </summary>
        public int ThrowOnEndNumber { get; set; }

        /// <summary>
        /// 1-based index (cumulative) of the ClampHPToMax call that throws <see cref="InvalidOperationException"/>
        /// (stands in for a death subscriber that throws); 0 means never. The throwing clamp is recorded, not forwarded.
        /// </summary>
        public int ThrowOnClampHpNumber { get; set; }

        /// <summary>
        /// When true, BeginStatBatch / EndStatBatch are also appended to <see cref="Calls"/>. Off by default so
        /// tests written before Story 006 that assert exact call sequences or counts keep their meaning; the
        /// counters below always count.
        /// </summary>
        public bool LogBatchCalls { get; set; }

        /// <summary>Number of BeginStatBatch calls.</summary>
        public int BeginCalls { get; private set; }

        /// <summary>Number of EndStatBatch calls.</summary>
        public int EndCalls { get; private set; }

        /// <summary>Number of ClampHPToMax calls.</summary>
        public int ClampHpCalls { get; private set; }

        /// <summary>Number of ClampMPToMax calls.</summary>
        public int ClampMpCalls { get; private set; }

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
            if (ThrowOnAddNumber != 0 && AddCalls == ThrowOnAddNumber)
                throw new InvalidOperationException("RecordingBuffModifierLayer: injected add failure");
            if (FailOnAddNumber != 0 && AddCalls == FailOnAddNumber)
                return false;
            if (_innerLayer != null)
                return _innerLayer.TryAddBuffModifier(entityId, statId, entry);
            return true;
        }

        /// <inheritdoc/>
        public void RemoveBuffModifier(EntityID entityId, StatID statId, BuffID buffId)
        {
            RemoveCalls++;
            Calls.Add(new LayerCall(LayerCallKind.Remove, entityId, statId, buffId, default));
            if (ThrowOnRemoveNumber != 0 && RemoveCalls == ThrowOnRemoveNumber)
                throw new InvalidOperationException("RecordingBuffModifierLayer: injected remove failure");
            _innerLayer?.RemoveBuffModifier(entityId, statId, buffId);
        }

        /// <inheritdoc/>
        public void BeginStatBatch()
        {
            BeginCalls++;
            if (LogBatchCalls)
                Calls.Add(new LayerCall(LayerCallKind.BeginBatch, default, default, default, default));
            _innerLayer?.BeginStatBatch();
        }

        /// <inheritdoc/>
        public void EndStatBatch()
        {
            EndCalls++;
            if (LogBatchCalls)
                Calls.Add(new LayerCall(LayerCallKind.EndBatch, default, default, default, default));
            _innerLayer?.EndStatBatch();
            if (ThrowOnEndNumber != 0 && EndCalls == ThrowOnEndNumber)
                throw new InvalidOperationException("RecordingBuffModifierLayer: injected end failure");
        }

        /// <inheritdoc/>
        public void ClampHPToMax(EntityID entityId)
        {
            ClampHpCalls++;
            Calls.Add(new LayerCall(LayerCallKind.ClampHp, entityId, default, default, default));
            if (ThrowOnClampHpNumber != 0 && ClampHpCalls == ThrowOnClampHpNumber)
                throw new InvalidOperationException("RecordingBuffModifierLayer: injected clamp failure");
            _innerSink?.ClampHPToMax(entityId);
        }

        /// <inheritdoc/>
        public void ClampMPToMax(EntityID entityId)
        {
            ClampMpCalls++;
            Calls.Add(new LayerCall(LayerCallKind.ClampMp, entityId, default, default, default));
            _innerSink?.ClampMPToMax(entityId);
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
