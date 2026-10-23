using System;
using UnityEngine;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// Maps a <see cref="ZoneTemplateID"/> to its scene name and <see cref="ZoneDefinition"/> (ADR-009). Referenced
    /// from the zone server host through a serialized field; never loaded through <c>Resources</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// if (registry.TryResolve("StartingZone", out string scene, out ZoneDefinition definition)) { /* load scene */ }
    /// </code>
    /// </example>
    [CreateAssetMenu(fileName = "ZoneSceneRegistry", menuName = "IronGrind/Zone Scene Registry")]
    public sealed class ZoneSceneRegistry : ScriptableObject
    {
        /// <summary>One registry row.</summary>
        [Serializable]
        public struct Entry
        {
            [SerializeField] private ZoneTemplateID _templateId;
            [SerializeField] private string _sceneName;
            [SerializeField] private ZoneDefinition _definition;

            /// <summary>Creates a row.</summary>
            /// <param name="templateId">Template the row serves.</param>
            /// <param name="sceneName">Name of the scene to load.</param>
            /// <param name="definition">The zone definition.</param>
            public Entry(ZoneTemplateID templateId, string sceneName, ZoneDefinition definition)
            {
                _templateId = templateId;
                _sceneName = sceneName;
                _definition = definition;
            }

            /// <summary>The template this row serves.</summary>
            public ZoneTemplateID TemplateId => _templateId;

            /// <summary>Scene name to load.</summary>
            public string SceneName => _sceneName;

            /// <summary>The zone definition.</summary>
            public ZoneDefinition Definition => _definition;
        }

        [SerializeField] private Entry[] _entries;

        /// <summary>
        /// Resolves a <c>-zoneTemplate</c> value. Only the enum name matches (case-sensitive); numeric text, an
        /// unknown name, an undefined value, surrounding whitespace, a comma list, a known value without an entry,
        /// an empty scene name or a null definition return false. The first entry for a template wins: if it is
        /// incomplete the call returns false even when a later duplicate is complete. An entry whose definition
        /// describes a different template returns false and logs <c>ZoneDefinitionLoadFailed</c> as an error.
        /// </summary>
        /// <param name="templateName">The command-line value.</param>
        /// <param name="sceneName">The scene to load, or null.</param>
        /// <param name="definition">The definition, or null.</param>
        /// <returns>True when the template resolved completely.</returns>
        public bool TryResolve(string templateName, out string sceneName, out ZoneDefinition definition)
        {
            sceneName = null;
            definition = null;
            if (string.IsNullOrEmpty(templateName) || _entries == null)
            {
                return false;
            }

            // Enum.TryParse also accepts numbers, signs, surrounding whitespace and comma lists; only a bare name is wanted.
            char first = templateName[0];
            if (char.IsDigit(first) || first == '-' || first == '+'
                || char.IsWhiteSpace(first) || char.IsWhiteSpace(templateName[templateName.Length - 1])
                || templateName.IndexOf(',') >= 0)
            {
                return false;
            }

            if (!Enum.TryParse<ZoneTemplateID>(templateName, false, out ZoneTemplateID id)
                || !Enum.IsDefined(typeof(ZoneTemplateID), id))
            {
                return false;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].TemplateId != id)
                {
                    continue;
                }

                // First match wins: an incomplete first entry fails even when a later duplicate is complete.
                if (string.IsNullOrEmpty(_entries[i].SceneName) || _entries[i].Definition == null)
                {
                    return false;
                }

                if (_entries[i].Definition.TemplateId != id)
                {
                    Debug.LogError("[ZoneInstancing] ZoneDefinitionLoadFailed: registry entry " + templateName
                        + " points at a definition for template " + _entries[i].Definition.TemplateId);
                    return false;
                }

                sceneName = _entries[i].SceneName;
                definition = _entries[i].Definition;
                return true;
            }

            return false;
        }

        /// <summary>Test-only initialiser (EditMode tests see internals).</summary>
        internal void Initialize(Entry[] entries)
        {
            _entries = entries;
        }
    }
}
