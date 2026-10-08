using System;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Zone teardown loot flush (design/gdd/loot-table-system.md teardown edge case, AC-LT-19, CR-LT-9,
    /// CR-LT-9.1, CR-LT-10; Story 012). Zone teardown calls <see cref="FlushForZoneTeardown"/> and then
    /// <see cref="Dispose"/>. The flush settles every open auction, removes every ground item and
    /// drops the damage records, so nothing is left in the zone and no bid is left unresolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The control manifest's <c>IZoneScopedService</c> does not exist in code yet. It must be adopted
    /// here when Zone Instancing defines it; it is not invented in this class.
    /// </para>
    /// <para>
    /// The teardown sequence itself and its ordering guarantees (ADR-009) are owned by Zone Instancing.
    /// This class only runs the loot half of it.
    /// </para>
    /// </remarks>
    public sealed class LootTeardownCoordinator : IDisposable
    {
        private readonly ILootAuctionService _auctionService;
        private readonly IGroundItemService _groundItemService;
        private readonly ILootTableService _lootTableService;

        /// <summary>Creates the coordinator over the three loot services of one zone.</summary>
        /// <param name="auctionService">Resolves every open auction at teardown.</param>
        /// <param name="groundItemService">Removes every remaining ground item.</param>
        /// <param name="lootTableService">Drops the damage records.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public LootTeardownCoordinator(
            ILootAuctionService auctionService,
            IGroundItemService groundItemService,
            ILootTableService lootTableService)
        {
            _auctionService = auctionService ?? throw new ArgumentNullException(nameof(auctionService));
            _groundItemService = groundItemService ?? throw new ArgumentNullException(nameof(groundItemService));
            _lootTableService = lootTableService ?? throw new ArgumentNullException(nameof(lootTableService));
        }

        /// <summary>
        /// Runs the loot flush in order: <see cref="ILootAuctionService.ResolveAllForTeardown"/>,
        /// <see cref="IGroundItemService.DespawnAll"/>, <see cref="ILootTableService.Clear"/>. A step
        /// that throws is logged and the remaining steps still run, so one failure cannot leave the
        /// zone's loot behind.
        /// </summary>
        /// <param name="teardownTick">The server tick of the teardown.</param>
        public void FlushForZoneTeardown(uint teardownTick)
        {
            try
            {
                _auctionService.ResolveAllForTeardown(teardownTick);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }

            try
            {
                _groundItemService.DespawnAll();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }

            try
            {
                _lootTableService.Clear();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Disposes the auction service first, then the ground item service (the auction service
        /// listens to the ground item service's events). Safe to call more than once. It does not
        /// flush: call <see cref="FlushForZoneTeardown"/> first.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _auctionService.Dispose();
            }
            finally
            {
                _groundItemService.Dispose();
            }
        }
    }
}
