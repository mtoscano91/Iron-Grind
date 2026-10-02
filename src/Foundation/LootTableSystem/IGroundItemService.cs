using System;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Ground item lifecycle contract (design/gdd/loot-table-system.md CR-LT-12, the GroundItem state
    /// table, CR-LT-15 server authority). Extracted from <see cref="GroundItemService"/> so that
    /// <see cref="LootDropDistributor"/> depends on an interface (ADR-010 Tier 1). Events are ADR-010
    /// Tier 2: the network layer turns them into GroundItemSpawned / GroundItemDespawned messages.
    /// Driven only by the server tick, never wall-clock time. Disposable because the service
    /// subscribes to the Inventory System's change event (ADR-010 Decision 4): dispose it at zone
    /// teardown and do not tick it afterwards.
    /// </summary>
    public interface IGroundItemService : IDisposable
    {
        /// <summary>Raised synchronously inside <see cref="Spawn"/> or <see cref="SpawnAuction"/>, once per item, after the record is stored.</summary>
        event Action<GroundItemSpawnedEventArgs> OnGroundItemSpawned;

        /// <summary>Raised once per item from <see cref="Tick"/>, after the expired record has been removed.</summary>
        event Action<GroundItemDespawnedEventArgs> OnGroundItemDespawned;

        /// <summary>
        /// Raised from <see cref="Tick"/> when the assignee's proximity pickup fails with a full bag
        /// while inside the pickup radius (CR-LT-13.2): once per blocked entry, never for an in-radius
        /// retry that fails again, and not when the item expires on that same tick.
        /// </summary>
        event Action<BagFullPickupBlockedEventArgs> OnBagFullPickupBlocked;

        /// <summary>
        /// Raised once from <see cref="Tick"/> for an <see cref="GroundItemState.Assigned"/> item on the
        /// tick where <c>expiryTick - currentTick</c> equals <c>EXPIRY_WARNING_TICKS</c> (CR-LT-13.3),
        /// after the pickup step. An item that was picked up, or is in another state, gets none.
        /// </summary>
        event Action<GroundItemExpiryWarningEventArgs> OnGroundItemExpiryWarning;

        /// <summary>
        /// Spawns a ground item in <see cref="GroundItemState.Spawning"/> with
        /// <c>expiryTick = spawnTick + GROUND_ITEM_TTL_TICKS</c> (CR-LT-12; wraparound-safe), stores it,
        /// then raises <see cref="OnGroundItemSpawned"/> synchronously. An invalid
        /// <paramref name="itemId"/> or <paramref name="assignedTo"/> (value 0) logs one error and
        /// returns <see cref="GroundItemID.Invalid"/> with no record, no event and no ID consumed.
        /// </summary>
        /// <remarks>
        /// Does not throw: an exception thrown by a spawn subscriber is logged and not rethrown, so
        /// the caller always receives the ID of the record that was stored. Subscribers after the
        /// throwing one on the same event do not receive that event.
        /// </remarks>
        /// <param name="itemId">The dropped item.</param>
        /// <param name="position">World position of the item.</param>
        /// <param name="assignedTo">The character the item is assigned to.</param>
        /// <param name="spawnTick">The current server tick.</param>
        /// <returns>The new ground item's ID, or <see cref="GroundItemID.Invalid"/>.</returns>
        GroundItemID Spawn(ItemID itemId, UnityEngine.Vector3 position, CharacterID assignedTo, uint spawnTick);

        /// <summary>
        /// Spawns a Rare drop auction item (CR-LT-8): like <see cref="Spawn"/>, but the item has no
        /// assignee (<see cref="CharacterID.Invalid"/>), belongs to <paramref name="partyId"/>, and
        /// <see cref="OnGroundItemSpawned"/> carries <c>IsAuction = true</c> and the party. An invalid
        /// <paramref name="itemId"/> or <see cref="PartyID.Uninitialized"/> logs one error and returns
        /// <see cref="GroundItemID.Invalid"/> with no record and no event.
        /// </summary>
        /// <param name="itemId">The dropped item.</param>
        /// <param name="position">World position of the item.</param>
        /// <param name="partyId">The party whose members may bid.</param>
        /// <param name="spawnTick">The current server tick.</param>
        /// <returns>The new ground item's ID, or <see cref="GroundItemID.Invalid"/>.</returns>
        GroundItemID SpawnAuction(ItemID itemId, UnityEngine.Vector3 position, PartyID partyId, uint spawnTick);

        /// <summary>
        /// Advances every live item for this server tick, in four steps. (1) An item leaves
        /// <see cref="GroundItemState.Spawning"/> on the first tick that is newer than its spawn tick
        /// (wraparound-safe; a tick that is not newer leaves it in place); an auction item becomes
        /// <see cref="GroundItemState.Auctioning"/> with <c>WindowCloseTick = currentTick +
        /// AUCTION_WINDOW_TICKS</c>, any other item <see cref="GroundItemState.Assigned"/>. (2) An assignee who has
        /// just entered the pickup radius of an <see cref="GroundItemState.Assigned"/> item triggers
        /// an automatic pickup attempt (CR-LT-7): success removes the item, a failed result leaves
        /// it assigned with its expiry tick unchanged (CR-LT-13). A full bag raises
        /// <see cref="OnBagFullPickupBlocked"/> and marks the item blocked; while the assignee stays
        /// inside the radius, a tick after their inventory changed with a free slot retries the
        /// pickup (CR-LT-13.2). Within a tick the in-radius retries run before the entry pickups.
        /// The "inventory changed" record is taken at the start of each tick; a change raised
        /// during a tick is kept for the next one. (3) An
        /// <see cref="GroundItemState.Assigned"/> item whose remaining ticks equal
        /// <c>EXPIRY_WARNING_TICKS</c> raises <see cref="OnGroundItemExpiryWarning"/> once.
        /// (4) Every remaining item whose expiry tick is reached is removed and then announced once
        /// through <see cref="OnGroundItemDespawned"/>. A pickup on the expiry tick is therefore
        /// attempted before the item despawns. Allocates nothing on a tick where no pickup
        /// triggers, no retry is due, no warning is raised and nothing expires.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The pickup trigger is an outside-to-inside transition, measured in 3D with the boundary
        /// inside; an assignee with no known position counts as outside. An assignee who stays
        /// inside after a failed pickup is retried only by the in-radius retry rule above.
        /// </para>
        /// <para>
        /// A pickup may call back into this service (the Inventory System raises its events
        /// synchronously). An item in <see cref="GroundItemState.Claiming"/> — only visible to such a
        /// re-entrant call — is not expired. If the pickup call throws, its outcome is unknown: the
        /// item is removed and announced through <see cref="OnGroundItemDespawned"/>, so it can
        /// never be delivered twice.
        /// </para>
        /// <para>
        /// Step 4 holds back an <see cref="GroundItemState.Assigned"/> item that has had a pickup
        /// fail on a full bag while its assignee's client is backgrounded and its pause budget is not
        /// used up (CR-LT-13.1; see <see cref="NotifyClientBackgrounded"/>). The result does not
        /// depend on whether a foreground notice or the tick comes first within a server tick.
        /// Every other non-terminal state expires in step 4.
        /// </para>
        /// <para>
        /// An <see cref="GroundItemState.Auctioning"/> item is never picked up, warned about or
        /// paused. For now it simply despawns at its expiry tick like any other item, also after
        /// its bid window has closed; Story 011 must branch on the state in step 4 so that an
        /// auction is resolved instead of despawned.
        /// A story that reassigns an item must reset its per-assignment data — the "assignee was
        /// inside" flag (or the new assignee's first entry is missed), the bag-full flag, and the
        /// pause budget.
        /// </para>
        /// <para>
        /// The order of the despawn events of items that expire on the same tick is unspecified. An
        /// exception thrown by a despawn subscriber is logged and not rethrown, so every expired item
        /// is still announced.
        /// </para>
        /// </remarks>
        /// <param name="currentTick">The current server tick.</param>
        void Tick(uint currentTick);

        /// <summary>
        /// Records that the character's client went to the background at server tick
        /// <paramref name="tick"/> (CR-LT-13.1). A second call before the foreground keeps the first
        /// tick. While backgrounded, a bag-full item of this character with pause budget left is held
        /// past its expiry tick (up to the remaining budget). An invalid character ID logs one error.
        /// Call it from the tick loop after the network layer decodes <c>ClientBackgrounded</c>, never
        /// from a network handler directly (ADR-010 Decision 5).
        /// </summary>
        /// <param name="characterId">The character whose client was backgrounded.</param>
        /// <param name="tick">The server tick of the message.</param>
        void NotifyClientBackgrounded(CharacterID characterId, uint tick);

        /// <summary>
        /// Ends the character's background period at server tick <paramref name="tick"/> (CR-LT-13.1):
        /// each live bag-full item assigned to the character has its expiry tick extended by
        /// <c>min(pausedTicks, PauseBudgetRemaining)</c> and its budget reduced by the same amount.
        /// Ignored when no background was recorded; a tick before the recorded background tick extends
        /// nothing. Call it from the tick loop after the network layer decodes
        /// <c>ClientForegrounded</c>, never from a network handler directly (ADR-010 Decision 5).
        /// </summary>
        /// <param name="characterId">The character whose client returned to the foreground.</param>
        /// <param name="tick">The server tick of the message.</param>
        void NotifyClientForegrounded(CharacterID characterId, uint tick);

        /// <summary>
        /// Ends the character's background period because the client disconnected or the character
        /// left the zone at server tick <paramref name="tick"/>: the pause earned up to that tick is
        /// booked exactly as <see cref="NotifyClientForegrounded"/> books it, and the item's timer runs
        /// normally from then on. The network layer must call this on every disconnect and zone
        /// leave; otherwise a background record outlives the session and a later background of the
        /// same character is measured from the old tick. Ignored when no background was recorded.
        /// Call it from the tick loop, never from a network handler directly (ADR-010 Decision 5).
        /// </summary>
        /// <param name="characterId">The character whose client disconnected or left the zone.</param>
        /// <param name="tick">The server tick of the disconnect.</param>
        void NotifyClientDisconnected(CharacterID characterId, uint tick);

        /// <summary>Looks up a live ground item. A despawned (removed) item returns false.</summary>
        /// <param name="id">The ground item ID.</param>
        /// <param name="item">A snapshot of the record when found.</param>
        /// <returns>True when the item is live.</returns>
        bool TryGetGroundItem(GroundItemID id, out GroundItem item);
    }
}
