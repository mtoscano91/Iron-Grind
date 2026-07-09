#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// In-memory implementation of <see cref="IZoneTestConfigurator"/>. Zone-scoped overrides
    /// (entry point, capacity, zone state) are keyed by <c>zoneInstanceId</c>; entity-scoped
    /// overrides (position, LastBeatServerTick, client OWL) are keyed by <c>entityId</c>, matching
    /// the interface's own parameter shape.
    /// </summary>
    /// <remarks>
    /// The real zone-lifecycle and entity-position systems these overrides ultimately feed do not
    /// exist yet (out of scope for this story — see the Zone Instancing and OWL Compensation
    /// epics). This class is a fully self-contained in-memory store for now; a future story wires
    /// its zone-scoped reads to the real zone-lifecycle system.
    /// </remarks>
    public sealed class ZoneTestConfigurator : IZoneTestConfigurator
    {
        private sealed class ZoneOverrides
        {
            public (short posX, short posY, short posZ)? EntryPoint;
            public int? Capacity;
            public ZoneState State = ZoneState.Empty;
        }

        private sealed class EntityOverrides
        {
            public (short posX, short posY, short posZ)? Position;
            public uint? LastBeatServerTick;
            public float? ClientOwlSeconds;
        }

        private readonly Dictionary<uint, ZoneOverrides> _zones = new Dictionary<uint, ZoneOverrides>();
        private readonly Dictionary<uint, EntityOverrides> _entities = new Dictionary<uint, EntityOverrides>();

        private ZoneOverrides GetOrCreateZone(uint zoneInstanceId)
        {
            if (!_zones.TryGetValue(zoneInstanceId, out ZoneOverrides overrides))
            {
                overrides = new ZoneOverrides();
                _zones[zoneInstanceId] = overrides;
            }
            return overrides;
        }

        private EntityOverrides GetOrCreateEntity(uint entityId)
        {
            if (!_entities.TryGetValue(entityId, out EntityOverrides overrides))
            {
                overrides = new EntityOverrides();
                _entities[entityId] = overrides;
            }
            return overrides;
        }

        /// <inheritdoc/>
        public void SetZoneEntryPoint(uint zoneInstanceId, short posX, short posY, short posZ)
        {
            GetOrCreateZone(zoneInstanceId).EntryPoint = (posX, posY, posZ);
        }

        /// <inheritdoc/>
        public void SetZoneCapacity(uint zoneInstanceId, int maxPlayers)
        {
            GetOrCreateZone(zoneInstanceId).Capacity = maxPlayers;
        }

        /// <inheritdoc/>
        public ZoneState GetCurrentZoneState(uint zoneInstanceId)
        {
            return _zones.TryGetValue(zoneInstanceId, out ZoneOverrides overrides) ? overrides.State : ZoneState.Empty;
        }

        /// <inheritdoc/>
        public (short posX, short posY, short posZ) GetEntityPosition(uint entityId)
        {
            if (_entities.TryGetValue(entityId, out EntityOverrides overrides) && overrides.Position.HasValue)
                return overrides.Position.Value;

            return (0, 0, 0);
        }

        /// <inheritdoc/>
        public void SetLastBeatServerTick(uint entityId, uint serverTickNumber)
        {
            GetOrCreateEntity(entityId).LastBeatServerTick = serverTickNumber;
        }

        /// <inheritdoc/>
        public void SetClientOWL(uint entityId, float owlSeconds)
        {
            GetOrCreateEntity(entityId).ClientOwlSeconds = owlSeconds;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Scoped only to <paramref name="zoneInstanceId"/> — removing that instance's dictionary
        /// entry cannot affect any other instance's entry. Entity-scoped overrides (position,
        /// LastBeatServerTick, client OWL) are not zone-scoped by the interface's own parameter
        /// shape and are therefore untouched by this call.
        /// </remarks>
        public void Reset(uint zoneInstanceId)
        {
            _zones.Remove(zoneInstanceId);
        }

        // ---------------------------------------------------------------------
        // Internal seams (not part of IZoneTestConfigurator): expose stored override
        // state for this story's own tests, and for future stories (real zone-lifecycle
        // system, OWL compensation) to read/drive. Visible to
        // IronGrind.Foundation.EditModeTests via InternalsVisibleTo
        // (src/Foundation/AssemblyInfo.cs).
        // ---------------------------------------------------------------------

        /// <summary>
        /// Test-only seam: sets <paramref name="zoneInstanceId"/>'s zone state directly, standing
        /// in for the real zone lifecycle system (which does not exist yet).
        /// </summary>
        internal void SetZoneStateForTesting(uint zoneInstanceId, ZoneState state)
        {
            GetOrCreateZone(zoneInstanceId).State = state;
        }

        /// <summary>
        /// Returns the raw capacity override for <paramref name="zoneInstanceId"/>, or
        /// <see langword="null"/> if never set — used to assert instance-scoping (AC-TH-3).
        /// </summary>
        internal int? GetZoneCapacityOverride(uint zoneInstanceId)
        {
            return _zones.TryGetValue(zoneInstanceId, out ZoneOverrides overrides) ? overrides.Capacity : null;
        }

        /// <summary>
        /// Returns the raw entry-point override for <paramref name="zoneInstanceId"/>, or
        /// <see langword="null"/> if never set — used to assert instance-scoping (AC-TH-3).
        /// </summary>
        internal (short posX, short posY, short posZ)? GetZoneEntryPointOverride(uint zoneInstanceId)
        {
            return _zones.TryGetValue(zoneInstanceId, out ZoneOverrides overrides) ? overrides.EntryPoint : null;
        }

        /// <summary>
        /// Returns the stored <c>LastBeatServerTick</c> override for <paramref name="entityId"/>,
        /// defaulting to 0 if never set (OWL compensation stories read this back).
        /// </summary>
        internal uint GetLastBeatServerTick(uint entityId)
        {
            return _entities.TryGetValue(entityId, out EntityOverrides overrides) && overrides.LastBeatServerTick.HasValue
                ? overrides.LastBeatServerTick.Value
                : 0u;
        }

        /// <summary>
        /// Returns the stored client OWL override for <paramref name="entityId"/>, defaulting to
        /// 0f if never set (OWL compensation stories read this back).
        /// </summary>
        internal float GetClientOWL(uint entityId)
        {
            return _entities.TryGetValue(entityId, out EntityOverrides overrides) && overrides.ClientOwlSeconds.HasValue
                ? overrides.ClientOwlSeconds.Value
                : 0f;
        }
    }
}
#endif
