using System;
using System.Collections.Generic;
using IronGrind.LootTableSystem;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// The mob definition registry, built once at startup from validated definitions and read-only afterwards
    /// (CR-AI-7). Only Enemy AI builds it; consumers see <see cref="IMobDefinitionRegistry"/>.
    /// </summary>
    public sealed class MobDefinitionRegistry : IMobDefinitionRegistry
    {
        private readonly Dictionary<MobTypeID, MobDefinition> _definitions;

        private MobDefinitionRegistry(Dictionary<MobTypeID, MobDefinition> definitions)
        {
            _definitions = definitions;
        }

        /// <summary>Number of accepted definitions.</summary>
        public int Count => _definitions.Count;

        /// <summary>
        /// Builds the registry. Each refused definition (failed rule or duplicate id; the first of a duplicate pair is kept)
        /// logs one error naming the type and the failed rules; each accepted definition with
        /// AttackCooldownTicks below the recommended minimum logs one warning.
        /// </summary>
        /// <remarks>
        /// This method is the ONLY place that logs refusals and warnings: the validator is pure, so a loader must not
        /// log the returned issues again. Unlike LootTableRegistry.TryCreate (all-or-nothing), Build refuses per
        /// definition (CR-AI-7: "refuse to spawn that mob type"); valid definitions are still registered.
        /// </remarks>
        /// <param name="definitions">Candidate definitions; null is treated as empty.</param>
        /// <param name="issuesOut">Optional; receives every issue found, including duplicates.</param>
        /// <returns>The built registry (possibly empty).</returns>
        public static MobDefinitionRegistry Build(
            IReadOnlyList<MobDefinition> definitions,
            List<MobDefinitionValidationIssue> issuesOut = null)
        {
            var accepted = new Dictionary<MobTypeID, MobDefinition>();
            if (definitions != null)
            {
                for (int i = 0; i < definitions.Count; i++)
                {
                    MobDefinition def = definitions[i];
                    List<MobDefinitionValidationIssue> issues = MobDefinitionValidator.Validate(def);

                    if (issues.Count == 0 && accepted.ContainsKey(def.MobTypeID))
                    {
                        issues.Add(new MobDefinitionValidationIssue(def.MobTypeID,
                            "MobTypeID: duplicate id; the first definition is kept."));
                    }

                    if (issues.Count > 0)
                    {
                        Debug.LogError(FormattableString.Invariant(
                            $"MobDefinition refused: {issues[0].MobTypeId} {Join(issues)}"));
                        if (issuesOut != null)
                        {
                            issuesOut.AddRange(issues);
                        }

                        continue;
                    }

                    if (MobDefinitionValidator.HasCooldownWarning(def))
                    {
                        Debug.LogWarning(FormattableString.Invariant(
                            $"MobDefinition {def.MobTypeID}: AttackCooldownTicks {def.AttackCooldownTicks} is below the recommended minimum of {MobDefinitionValidator.RECOMMENDED_MIN_COOLDOWN_TICKS}."));
                    }

                    accepted.Add(def.MobTypeID, def);
                }
            }

            return new MobDefinitionRegistry(accepted);
        }

        /// <inheritdoc/>
        public MobDefinition GetDefinition(MobTypeID id)
        {
            if (id == MobTypeID.Invalid)
            {
                return null;
            }

            return _definitions.TryGetValue(id, out MobDefinition def) ? def : null;
        }

        private static string Join(List<MobDefinitionValidationIssue> issues)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < issues.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(issues[i].Message);
            }

            return sb.ToString();
        }
    }
}
