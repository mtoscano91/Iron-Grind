namespace IronGrind.LootTableSystem
{
    /// <summary>One loot table validation error, naming the offending mob type, field and value.</summary>
    public readonly struct LootTableValidationIssue
    {
        /// <summary>Initializes a new issue.</summary>
        public LootTableValidationIssue(MobTypeID mobTypeId, string message)
        {
            MobTypeId = mobTypeId;
            Message = message;
        }

        /// <summary>The mob type whose table is at fault.</summary>
        public MobTypeID MobTypeId { get; }

        /// <summary>Human-readable description naming the field and the value.</summary>
        public string Message { get; }

        /// <inheritdoc/>
        public override string ToString() => $"[{MobTypeId}] {Message}";
    }
}
