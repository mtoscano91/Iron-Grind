using System;
using System.Collections.Generic;
using IronGrind.ServerLogic.ZoneInstancing;
using UnityEngine;

namespace IronGrind.ServerLogic.MobSpawning
{
    /// <summary>
    /// The spawn table of one zone template (CR-MS-2, TR-ms-001): the template it belongs to and its spawn points.
    /// Referenced by <see cref="ZoneDefinition.SpawnTable"/>; checked by <see cref="ZoneSpawnTableValidator"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "ZoneSpawnTable", menuName = "IronGrind/Zone Spawn Table")]
    public sealed class ZoneSpawnTable : ScriptableObject
    {
        [SerializeField] private ZoneTemplateID _templateId;
        [SerializeField] private SpawnPointEntry[] _entries = Array.Empty<SpawnPointEntry>();

        /// <summary>The zone template this table belongs to.</summary>
        public ZoneTemplateID TemplateId => _templateId;

        /// <summary>The spawn points as authored, read-only (never null).</summary>
        public IReadOnlyList<SpawnPointEntry> Entries => _entries ?? Array.Empty<SpawnPointEntry>();

        /// <summary>Test-only initialiser (EditMode tests see internals).</summary>
        internal void SetForTests(ZoneTemplateID templateId, SpawnPointEntry[] entries)
        {
            _templateId = templateId;
            _entries = entries;
        }
    }
}
