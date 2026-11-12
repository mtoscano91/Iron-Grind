using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>One authored spawn point handed to <see cref="SpawnPointValidator"/> (CR-NAV-13).</summary>
    public readonly struct SpawnPointInput
    {
        /// <summary>Creates a spawn point input.</summary>
        /// <param name="id">The SpawnPointID (mob-spawning.md).</param>
        /// <param name="position">The authored world-space position.</param>
        public SpawnPointInput(uint id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        /// <summary>The SpawnPointID.</summary>
        public uint Id { get; }

        /// <summary>The authored world-space position (not the sampled NavMesh hit).</summary>
        public Vector3 Position { get; }
    }
}
