namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Payload of <see cref="GroundItemService.OnGroundItemDespawned"/> (ADR-010 Tier 2; mirrors the
    /// GroundItemDespawned wire message in design/gdd/networking-wire-protocol.md).
    /// </summary>
    public readonly struct GroundItemDespawnedEventArgs
    {
        /// <summary>Creates the payload.</summary>
        public GroundItemDespawnedEventArgs(GroundItemID groundItemId)
        {
            GroundItemId = groundItemId;
        }

        /// <summary>The ground item that despawned.</summary>
        public GroundItemID GroundItemId { get; }
    }
}
