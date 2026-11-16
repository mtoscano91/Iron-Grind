using System.Collections.Generic;
using IronGrind.CharacterStats;
using UnityEngine;

namespace IronGrind.ServerLogic.Navigation
{
    /// <summary>
    /// Source of the committed (server-authoritative) mob positions that phase 1 of the tick feeds to
    /// <see cref="ZoneNavigationService.SyncAgentPositions(IReadOnlyDictionary{EntityID, Vector3})"/> (CR-NAV-12).
    /// Enemy AI owns mob position integration (phase 4) and is the real implementation;
    /// <c>MobRuntimeStore</c> implements it.
    /// </summary>
    public interface IMobCommittedPositionSource
    {
        /// <summary>Copies the committed world position of every mob into <paramref name="into"/>.</summary>
        /// <param name="into">The buffer to fill. The caller owns it and clears it before the call.</param>
        void CopyCommittedPositions(Dictionary<EntityID, Vector3> into);
    }
}
