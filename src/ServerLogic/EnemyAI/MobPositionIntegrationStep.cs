using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.ServerLogic.EnemyAI
{
    /// <summary>
    /// Phase 4 step (Story 009): applies the velocities cached by <see cref="EnemyAISystem.Run"/>. For each mob with a cached
    /// velocity, new position = current + (v.x, 0, v.z) x deltaTime (Y kept). A non-finite result keeps the last position and
    /// logs one warning per mob. Position and facing are written to the controller and to the <see cref="MobRuntimeStore"/>
    /// (facing for every mob, every tick). The cache is cleared after applying. Never calls the navigation provider.
    /// </summary>
    public sealed class MobPositionIntegrationStep : IZoneSimulationStep
    {
        private readonly EnemyAISystem _system;

        internal MobPositionIntegrationStep(EnemyAISystem system)
        {
            _system = system;
        }

        /// <inheritdoc/>
        public void Run(uint currentTick, float deltaTime)
        {
            IReadOnlyList<MobController> controllers = _system.Controllers;
            Vector3[] velocities = _system.Velocities;
            bool[] hasVelocity = _system.HasVelocity;
            bool[] warned = _system.Warned;
            MobRuntimeStore store = _system.Store;

            for (int i = 0; i < controllers.Count; i++)
            {
                MobController controller = controllers[i];
                if (hasVelocity[i])
                {
                    hasVelocity[i] = false;
                    Vector3 current = controller.Position;
                    Vector3 v = velocities[i];
                    var candidate = new Vector3(current.x + v.x * deltaTime, current.y, current.z + v.z * deltaTime);
                    if (IsFinite(candidate))
                    {
                        controller.Position = candidate;
                    }
                    else if (!warned[i])
                    {
                        warned[i] = true;
                        LogNonFinite(controller.EntityID, currentTick);
                    }
                }

                store.TrySetCommitted(controller.EntityID, controller.Position, (short)controller.FacingAngle);
            }
        }

        private static bool IsFinite(Vector3 p)
        {
            return !(float.IsNaN(p.x) || float.IsInfinity(p.x)
                || float.IsNaN(p.y) || float.IsInfinity(p.y)
                || float.IsNaN(p.z) || float.IsInfinity(p.z));
        }

        // Called at most once per mob, so the message is built only then.
        private static void LogNonFinite(EntityID id, uint tick)
        {
            try
            {
                Debug.LogWarning($"[MobPositionIntegrationStep] NonFinitePosition: mob={id} tick={tick}; position kept (logged once per mob).");
            }
            catch (Exception)
            {
                // Logging must not break the tick.
            }
        }
    }
}
