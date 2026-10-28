using System;
using IronGrind.Networking;

namespace IronGrind.EquipmentSystem
{
    /// <summary>
    /// Delivers <see cref="AppearanceChangedEvent"/> to every client of this zone, including the equipping player, when a
    /// character's <c>equipmentAppearanceFlags</c> byte changes (Equipment Story 009; design/gdd/equipment-system.md CR-EQS-11;
    /// networking-wire-protocol.md <c>AppearanceChangedEvent</c>; networking-channel-contract.md CCR-3; ADR-015 Decision 1; ADR-010).
    /// It subscribes to <see cref="IEquipmentService.OnAppearanceChanged"/>, encodes the 5-byte body into a reused buffer and calls
    /// <see cref="IZoneClientBroadcastSink.EnqueueToAllClients"/> with <c>isCapExempt: false</c> (CR-NET-7.7).
    /// </summary>
    /// <remarks>
    /// <para>The event is raised only when the byte changed (Story 005's raise rule), so nothing is sent for <c>NoChange</c>, a failure,
    /// an equip that leaves the byte the same, or a login restore (<c>RestoreSlots</c> raises nothing; the joining player's byte
    /// reaches others through the zone snapshot).</para>
    /// <para>The audience is this zone's clients only; the cross-zone relay is not involved. An exception from the sink is not caught
    /// here: <c>EquipmentService</c> contains handler exceptions when it raises the event and logs once through
    /// <c>Debug.LogException</c>, and the equip result is unchanged.</para>
    /// <para>The body buffer is reused for every send, so the sink must copy the span before it returns (the
    /// <see cref="IZoneClientBroadcastSink"/> contract: the body is valid only during the call).</para>
    /// <para>Tick thread only. One instance per zone. Call <see cref="Dispose"/> to unsubscribe. Constructed by the server bootstrap
    /// once the real <c>EquipmentService</c> is composed (the zone composition currently uses a stand-in service, so it is not wired yet).</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using var sender = new AppearanceChangedSender(equipmentService, zoneClientSink);
    /// </code>
    /// </example>
    public sealed class AppearanceChangedSender : IDisposable
    {
        private readonly IEquipmentService _service;
        private readonly IZoneClientBroadcastSink _sink;
        private readonly byte[] _body = new byte[AppearanceChangedEvent.BodySize];
        private bool _disposed;

        /// <summary>Creates the sender and subscribes it to <see cref="IEquipmentService.OnAppearanceChanged"/>.</summary>
        /// <param name="service">The Equipment service whose appearance event is observed; not null.</param>
        /// <param name="sink">Delivers a message to every client of this zone (ADR-015 Decision 1); not null.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AppearanceChangedSender(IEquipmentService service, IZoneClientBroadcastSink sink)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _service.OnAppearanceChanged += OnAppearanceChanged;
        }

        /// <summary>Unsubscribes from the service. Safe to call more than once; after it nothing is sent.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _service.OnAppearanceChanged -= OnAppearanceChanged;
        }

        // CR-EQS-11: runs on the tick, from the service's raise at the end of Equip / Unequip.
        private void OnAppearanceChanged(EquipmentAppearanceChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            int written = AppearanceChangedEventCodec.WriteBody(_body, e.EntityId.RawValue, e.Flags);
            _sink.EnqueueToAllClients(AppearanceChangedEvent.MessageTypeId, new ReadOnlySpan<byte>(_body, 0, written), isCapExempt: false);
        }
    }
}
