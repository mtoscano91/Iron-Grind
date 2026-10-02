using System;
using System.Collections.Generic;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Assigns the drops of a resolved kill to party members by round-robin and spawns them as
    /// ground items (design/gdd/loot-table-system.md CR-LT-5 routing, CR-LT-6 round-robin,
    /// CR-LT-10 fallback, CR-LT-11 solo rare drop; party side design/gdd/party-system.md CR-PS-7).
    /// The Party System owns the round-robin cursor: this class only reads it and asks the Party
    /// System to advance it (ADR-010: never mutate another system's state). Implements Story 006.
    /// </summary>
    public sealed class LootDropDistributor : ILootDropSink
    {
        private const int UNKNOWN_PARTY_SIZE = -1;
        private const int MIN_AUCTION_PARTY_SIZE = 2;

        private readonly IPartyService _partyService;
        private readonly LootEquipmentCache _equipmentCache;
        private readonly IGroundItemService _groundItems;
        private readonly Func<uint> _currentTick;

        /// <summary>Creates the distributor.</summary>
        /// <param name="partyService">Supplies members, the cursor read and the cursor advance.</param>
        /// <param name="equipmentCache">Classifies each drop (CR-LT-5).</param>
        /// <param name="groundItems">Spawns the ground items.</param>
        /// <param name="currentTick">Server tick source (same one <see cref="PartyTagTracker"/> takes).</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public LootDropDistributor(
            IPartyService partyService,
            LootEquipmentCache equipmentCache,
            IGroundItemService groundItems,
            Func<uint> currentTick)
        {
            _partyService = partyService ?? throw new ArgumentNullException(nameof(partyService));
            _equipmentCache = equipmentCache ?? throw new ArgumentNullException(nameof(equipmentCache));
            _groundItems = groundItems ?? throw new ArgumentNullException(nameof(groundItems));
            _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        }

        /// <summary>
        /// Handles each drop in list order. Common drops always take the round-robin path (CR-LT-5,
        /// CR-LT-6). A Rare drop goes through <c>BeginRareDrop</c>: a party of 2 or more opens an
        /// auction (CR-LT-8) and takes no round-robin turn; any smaller party uses the common path
        /// (CR-LT-11). The tick is read once per call, and the party size at most once.
        /// </summary>
        /// <remarks>
        /// Each item is handled on its own: the kill's gold is already paid and its damage record
        /// already cleared when this is called, so an exception from a party call on one item is
        /// logged and the remaining items are still distributed. An item with an invalid ID logs
        /// one error and does not consume a round-robin turn.
        /// </remarks>
        /// <param name="mobEntityId">The mob that died.</param>
        /// <param name="winningParty">The party that owns the loot tag.</param>
        /// <param name="drops">The dropped item IDs, in table entry order.</param>
        /// <param name="position">The mob's world position at death.</param>
        public void OnDropsResolved(EntityID mobEntityId, PartyID winningParty, IReadOnlyList<ItemID> drops, UnityEngine.Vector3 position)
        {
            uint tick = _currentTick();
            int partySize = UNKNOWN_PARTY_SIZE; // read lazily, only when a Rare item is present

            for (int i = 0; i < drops.Count; i++)
            {
                ItemID item = drops[i];
                try
                {
                    if (item == ItemID.Invalid)
                    {
                        UnityEngine.Debug.LogError(
                            $"[LootDropDistributor] Drop {i} of {mobEntityId} has an invalid item ID (0); item not spawned.");
                        continue;
                    }

                    if (_equipmentCache.Classify(item) == DropTier.Rare)
                    {
                        if (partySize == UNKNOWN_PARTY_SIZE)
                        {
                            partySize = ReadPartySize(winningParty);
                        }
                        BeginRareDrop(mobEntityId, winningParty, item, position, tick, partySize);
                    }
                    else
                    {
                        AssignByRoundRobin(mobEntityId, winningParty, item, position, tick);
                    }
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }

        // A null list counts as an empty party, like LootTableService.ResolveMobDrop treats it.
        private int ReadPartySize(PartyID party)
        {
            IReadOnlyList<CharacterID> members = _partyService.GetPartyMembers(party);
            return members == null ? 0 : members.Count;
        }

        private void BeginRareDrop(EntityID mobEntityId, PartyID party, ItemID item, UnityEngine.Vector3 position, uint tick, int partySize)
        {
            if (partySize >= MIN_AUCTION_PARTY_SIZE)
            {
                // CR-LT-8: a Rare drop in a party of 2 or more opens an auction for the whole party.
                // It has no assignee and takes no round-robin turn: the cursor is not read or advanced.
                // The result is not checked: item and party are both valid here.
                _groundItems.SpawnAuction(item, position, party, tick);
                return;
            }

            // CR-LT-11: a solo player gets the Rare drop through the common path. A size below 1
            // (party gone) also lands here and ends in the invalid-member error, never an auction.
            AssignByRoundRobin(mobEntityId, party, item, position, tick);
        }

        // CR-LT-6: read the cursor, resolve the member, spawn, then ask the Party System to advance
        // the cursor exactly once (also when the slot was invalid, so the rotation never stalls).
        private void AssignByRoundRobin(EntityID mobEntityId, PartyID party, ItemID item, UnityEngine.Vector3 position, uint tick)
        {
            int cursor = _partyService.GetRrNextIndex(party);
            CharacterID assignee = _partyService.GetMemberAtIndex(party, cursor);

            if (assignee == CharacterID.Invalid)
            {
                UnityEngine.Debug.LogError(
                    $"[LootDropDistributor] No member at round-robin cursor {cursor} of {party} for {item} dropped by {mobEntityId}; item not spawned.");
            }
            else
            {
                // The result is not checked: item and assignee are both valid here, so Spawn
                // cannot return GroundItemID.Invalid.
                _groundItems.Spawn(item, position, assignee, tick);
            }

            _partyService.AdvanceRrNextIndex(party);
        }
    }
}
