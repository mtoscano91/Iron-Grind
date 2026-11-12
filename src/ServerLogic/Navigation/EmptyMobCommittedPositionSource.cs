using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Stand-in for the Enemy AI committed position source: copies nothing, so no agent position is synced.
    /// Story 010 wires it until Enemy AI exists.
    /// </summary>
    public sealed class EmptyMobCommittedPositionSource : IMobCommittedPositionSource
    {
        /// <inheritdoc />
        public void CopyCommittedPositions(Dictionary<EntityID, Vector3> into)
        {
        }
    }
}
