using System;
using IronGrind.CharacterPersistence;
using IronGrind.ItemDatabase;
using IronGrind.Networking;
using UnityEngine;

namespace IronGrind.EnhancementSystem
{
    /// <summary>
    /// Sends <see cref="ServerBroadcast_Enhancement9"/> to every online player when an enhancement reaches +9
    /// (Enhancement Story 016; design/gdd/enhancement-system.md CR-ENH-14, CR-ENH-15 step 8, UI-ENH-3, EC-ENH-8, AC-ENH-18;
    /// ADR-015 Decision 1 and 6, ADR-011, ADR-010). It subscribes to
    /// <see cref="EnhancementService.OnEnhancementBroadcastLevelReached"/>, which <see cref="EnhancementService.CompleteAttempt"/>
    /// raises only after the commit, resolves the player and item display names, encodes the body and calls
    /// <see cref="IServerWideBroadcaster.Broadcast"/> (this zone's clients through the outbox, then the other zones through the relay).
    /// </summary>
    /// <remarks>
    /// <para>A missing or empty character name, or a missing item definition or one with a null or empty display name, logs one
    /// warning (<c>NameMissing</c>, with <c>nameFound</c> and <c>itemFound</c> saying which lookup failed) and sends nothing;
    /// the outcome and the owner's result are unaffected (EC-ENH-8). An exception from the broadcaster is contained by the service's event raise.</para>
    /// <para>Tick thread only. One instance per zone. Call <see cref="Dispose"/> to unsubscribe.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var sender = new Enhancement9BroadcastSender(service, broadcaster, characterPersistence, itemDatabase);
    /// </code>
    /// </example>
    public sealed class Enhancement9BroadcastSender : IDisposable
    {
        private const string LOG_PREFIX = "[Enhancement9BroadcastSender]";

        private readonly EnhancementService _service;
        private readonly IServerWideBroadcaster _broadcaster;
        private readonly ICharacterNameLookup _names;
        private readonly IItemDatabase _items;
        private readonly byte[] _body = new byte[ServerBroadcast_Enhancement9.MaxBodySize];
        private bool _disposed;

        /// <summary>Creates the sender and subscribes it to <see cref="EnhancementService.OnEnhancementBroadcastLevelReached"/> (CR-ENH-14).</summary>
        /// <param name="service">The Enhancement service whose +9 event is observed; not null.</param>
        /// <param name="broadcaster">Delivers the message to every zone (ADR-015); not null.</param>
        /// <param name="names">Resolves the character's name; not null.</param>
        /// <param name="items">Resolves the item's display name; not null.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Enhancement9BroadcastSender(
            EnhancementService service, IServerWideBroadcaster broadcaster, ICharacterNameLookup names, IItemDatabase items)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
            _names = names ?? throw new ArgumentNullException(nameof(names));
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _service.OnEnhancementBroadcastLevelReached += OnLevelReached;
        }

        /// <summary>Unsubscribes from the service. Safe to call more than once; after it no broadcast is sent.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.OnEnhancementBroadcastLevelReached -= OnLevelReached;
        }

        // CR-ENH-14, CR-ENH-15 step 8: runs on the tick, after the commit, from CompleteAttempt.
        private void OnLevelReached(EnhancementBroadcastEventArgs e)
        {
            bool nameFound = _names.TryGetCharacterName(e.CharacterId, out string playerName) && !string.IsNullOrEmpty(playerName);
            string itemName = null;
            bool itemFound = _items.TryGetItem(e.ItemId, out ItemDefinition definition) && definition != null
                && !string.IsNullOrEmpty(itemName = definition.DisplayName);
            if (!nameFound || !itemFound)
            {
                // EC-ENH-8: the broadcast is best effort; the outcome stands.
                Debug.LogWarning($"{LOG_PREFIX} NameMissing: charId={e.CharacterId} itemId={e.ItemId} "
                    + $"nameFound={(nameFound ? "true" : "false")} itemFound={(itemFound ? "true" : "false")} - no broadcast.");
                return;
            }

            int written = ServerBroadcast_Enhancement9Codec.WriteBody(_body, playerName, itemName);
            _broadcaster.Broadcast(ServerBroadcast_Enhancement9.MessageTypeId, new ReadOnlySpan<byte>(_body, 0, written));
        }
    }
}
