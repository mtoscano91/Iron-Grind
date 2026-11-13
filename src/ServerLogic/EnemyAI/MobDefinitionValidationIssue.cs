using IronGrind.LootTableSystem;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>One mob definition validation error (CR-AI-7), naming the mob type, field and value.</summary>
    public readonly struct MobDefinitionValidationIssue
    {
        /// <summary>Initializes a new issue.</summary>
        public MobDefinitionValidationIssue(MobTypeID mobTypeId, string message)
        {
            MobTypeId = mobTypeId;
            Message = message;
        }

        /// <summary>The mob type whose definition is at fault.</summary>
        public MobTypeID MobTypeId { get; }

        /// <summary>
        /// Human-readable description. Always starts with the failed field name followed by a colon
        /// (for example <c>"AttackWindUpTicks: 11 ..."</c>) so callers and tests can match the rule.
        /// </summary>
        public string Message { get; }

        /// <inheritdoc/>
        public override string ToString() => $"[{MobTypeId}] {Message}";
    }
}
