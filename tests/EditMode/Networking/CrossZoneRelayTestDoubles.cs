using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.Tests.EditMode.Networking
{
    /// <summary>
    /// The in-memory relay bus (ADR-015, Networking Core Story 040): connects the publishers and receivers of several
    /// in-process test zones. It goes through <see cref="RelayEnvelopeCodec"/> exactly like the Npgsql transport will:
    /// <c>Publish</c> encodes the envelope with the publishing zone's origin and hands the payload text to every
    /// registered zone's <see cref="CrossZoneRelayReceiver"/>, the publisher's own included, so the echo drop is exercised.
    /// A test double: it lives in <c>tests/</c> because nothing in <c>src/</c> needs it.
    /// </summary>
    internal sealed class InMemoryCrossZoneRelayBus
    {
        private readonly List<CrossZoneRelayReceiver> _receivers = new List<CrossZoneRelayReceiver>();

        /// <summary>Every payload that went through the bus, in order.</summary>
        public readonly List<string> Payloads = new List<string>();

        /// <summary>Registers a zone and returns its publisher.</summary>
        public ICrossZoneRelayPublisher Register(CrossZoneRelayOrigin origin, CrossZoneRelayReceiver receiver)
        {
            _receivers.Add(receiver);
            return new ZonePublisher(this, origin);
        }

        /// <summary>Hands a payload text to every registered receiver. Tests call it to inject a hand-built payload.</summary>
        public void Deliver(string payload)
        {
            Payloads.Add(payload);
            for (int i = 0; i < _receivers.Count; i++)
            {
                _receivers[i].OnPayload(payload);
            }
        }

        private sealed class ZonePublisher : ICrossZoneRelayPublisher
        {
            private readonly InMemoryCrossZoneRelayBus _bus;
            private readonly CrossZoneRelayOrigin _origin;

            public ZonePublisher(InMemoryCrossZoneRelayBus bus, CrossZoneRelayOrigin origin)
            {
                _bus = bus;
                _origin = origin;
            }

            public void Publish(ushort messageTypeId, ReadOnlySpan<byte> body)
            {
                _bus.Deliver(RelayEnvelopeCodec.Encode(messageTypeId, _origin.Value, body));
            }
        }
    }

    /// <summary>One test zone: outbox with a recording send seam, broadcaster, inbox, receiver and pipeline on a bus.</summary>
    internal sealed class RelayTestZone
    {
        public readonly OutboxRecordingSendSeam Seam = new OutboxRecordingSendSeam();
        public readonly ClientMessageOutbox Outbox;
        public readonly CrossZoneRelayAllowlist Allowlist;
        public readonly CrossZoneRelayOrigin Origin;
        public readonly CrossZoneRelayInbox Inbox;
        public readonly CrossZoneRelayReceiver Receiver;
        public readonly ServerWideBroadcaster Broadcaster;
        public readonly InboundSettableTickSource TickSource = new InboundSettableTickSource();
        public readonly ZoneTickPipeline Pipeline;

        public RelayTestZone(InMemoryCrossZoneRelayBus bus, int inboxCapacity, params uint[] clientIds)
        {
            Outbox = new ClientMessageOutbox(Seam);
            for (int i = 0; i < clientIds.Length; i++)
            {
                Outbox.AddClient(clientIds[i]);
            }

            Allowlist = CrossZoneRelayAllowlist.CreateProduction();
            Origin = new CrossZoneRelayOrigin();
            Inbox = new CrossZoneRelayInbox(Allowlist.LargestMaxBodyBytes, inboxCapacity);
            Receiver = new CrossZoneRelayReceiver(Allowlist, Inbox, Origin);
            ICrossZoneRelayPublisher publisher = bus.Register(Origin, Receiver);
            Broadcaster = new ServerWideBroadcaster(Outbox, publisher, Allowlist);
            Pipeline = new ZoneTickPipeline(
                new RelayRecordingCompletionQueue(null),
                Inbox,
                new ZoneClientRelayHandler(Outbox),
                new RelayRecordingDispatcher(null),
                new RelayRecordingSimulation(null),
                Outbox,
                TickSource);
        }

        /// <summary>Runs one pipeline tick (relay step, then the outbound flush).</summary>
        public void Tick(uint tick)
        {
            TickSource.Tick = tick;
            Pipeline.Tick(0.05f);
        }

        public int SendCount(uint clientId) => Bodies(clientId).Count;

        /// <summary>The bodies sent to a client (envelope removed), in send order.</summary>
        public List<byte[]> Bodies(uint clientId)
        {
            var bodies = new List<byte[]>();
            foreach (OutboxRecordedSend send in Seam.Sends)
            {
                if (send.ClientId == clientId)
                {
                    var body = new byte[send.Bytes.Length - ServerMessageEnvelope.WireSize];
                    Array.Copy(send.Bytes, ServerMessageEnvelope.WireSize, body, 0, body.Length);
                    bodies.Add(body);
                }
            }

            return bodies;
        }
    }

    /// <summary>A body and the type it arrived with.</summary>
    internal readonly struct RelayReceived
    {
        public readonly ushort MessageTypeId;
        public readonly byte[] Body;

        public RelayReceived(ushort messageTypeId, byte[] body)
        {
            MessageTypeId = messageTypeId;
            Body = body;
        }
    }

    /// <summary>A relay handler that records every entry; can throw on one call and run a hook on every call.</summary>
    internal sealed class RelayRecordingHandler : ICrossZoneRelayHandler
    {
        public readonly List<RelayReceived> Received = new List<RelayReceived>();

        public List<string> Log;
        public int ThrowOnCall = -1;
        public Action<int> Hook;

        public void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            int call = Received.Count;
            Received.Add(new RelayReceived(messageTypeId, body.ToArray()));
            Log?.Add("Handler(" + call + ")");
            Hook?.Invoke(call);
            if (call == ThrowOnCall)
            {
                throw new InvalidOperationException("relay handler fake");
            }
        }
    }

    /// <summary>A relay handler that only counts, so it allocates nothing.</summary>
    internal sealed class RelayCountingHandler : ICrossZoneRelayHandler
    {
        public int Count;

        public void OnRelayed(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            Count++;
        }
    }

    /// <summary>A broadcast sink that records its calls in a shared log.</summary>
    internal sealed class RelayRecordingSink : IZoneClientBroadcastSink
    {
        public readonly List<string> Log;

        public int Calls;
        public bool Throw;

        public RelayRecordingSink(List<string> log)
        {
            Log = log;
        }

        public void EnqueueToAllClients(ushort messageTypeId, ReadOnlySpan<byte> body, bool isCapExempt)
        {
            Calls++;
            Log.Add("EnqueueToAll(0x" + messageTypeId.ToString("X4") + ", exempt=" + isCapExempt + ", length=" + body.Length + ")");
            if (Throw)
            {
                throw new InvalidOperationException("sink fake");
            }
        }
    }

    /// <summary>A publisher that records its calls in a shared log and can throw.</summary>
    internal sealed class RelayRecordingPublisher : ICrossZoneRelayPublisher
    {
        public readonly List<string> Log;

        public int Calls;
        public bool Throw;

        public RelayRecordingPublisher(List<string> log)
        {
            Log = log;
        }

        public void Publish(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            Calls++;
            Log?.Add("Publish(0x" + messageTypeId.ToString("X4") + ")");
            if (Throw)
            {
                throw new InvalidOperationException("publisher fake");
            }
        }
    }

    /// <summary>A publisher that does nothing, so it allocates nothing.</summary>
    internal sealed class RelayNoOpPublisher : ICrossZoneRelayPublisher
    {
        public void Publish(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
        }
    }

    /// <summary>A relay inbox that logs the drain in a shared log and can throw.</summary>
    internal sealed class RelayRecordingInbox : ICrossZoneRelayInbox
    {
        private readonly List<string> _log;

        public bool ThrowOnDrain;
        public bool ThrowOnAdd;

        public RelayRecordingInbox(List<string> log)
        {
            _log = log;
        }

        public bool TryAdd(ushort messageTypeId, ReadOnlySpan<byte> body)
        {
            if (ThrowOnAdd)
            {
                throw new InvalidOperationException("inbox add fake");
            }

            return false;
        }

        public int Drain(uint currentTick, ICrossZoneRelayHandler handler)
        {
            _log?.Add("Relay(" + currentTick + ")");
            if (ThrowOnDrain)
            {
                throw new InvalidOperationException("boom");
            }

            return 0;
        }
    }

    /// <summary>A disconnect requester that adds a new client to the outbox when asked to disconnect (re-entrancy probe).</summary>
    internal sealed class RelayAddClientRequester : IClientDisconnectRequester
    {
        public ClientMessageOutbox Outbox;
        public uint ClientToAdd;

        public void Disconnect(uint clientId)
        {
            Outbox.AddClient(ClientToAdd);
        }
    }

    // ---- pipeline step recorders. A null log records nothing and allocates nothing. -----------------------

    internal sealed class RelayRecordingCompletionQueue : ITickCompletionQueue
    {
        private readonly List<string> _log;

        public RelayRecordingCompletionQueue(List<string> log)
        {
            _log = log;
        }

        public int InFlightCount => 0;

        public void Drain(uint currentTick)
        {
            _log?.Add("Completion(" + currentTick + ")");
        }

        public void Track<T>(Task<T> task, CancellationTokenSource cancellation, Action<TickTaskResult<T>> onCompletedOnTick)
        {
            throw new NotSupportedException();
        }

        public void DrainOnShutdown(TimeSpan timeout)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class RelayRecordingDispatcher : IInboundRequestDispatcher
    {
        private readonly List<string> _log;

        public RelayRecordingDispatcher(List<string> log)
        {
            _log = log;
        }

        public void DispatchTick(uint currentTick)
        {
            _log?.Add("Requests(" + currentTick + ")");
        }

        public void Register(InboundRequestDescriptor descriptor, InboundRequestHandler handler)
        {
            throw new NotSupportedException();
        }

        public void RegisterConnectionLevel(ConnectionMessageDescriptor descriptor, IConnectionMessageSink sink)
        {
            throw new NotSupportedException();
        }

        public void Seal()
        {
            throw new NotSupportedException();
        }

        public bool AddConnection(uint clientId)
        {
            throw new NotSupportedException();
        }

        public void RemoveConnection(uint clientId)
        {
            throw new NotSupportedException();
        }

        public int HeldCount(CharacterID charId)
        {
            throw new NotSupportedException();
        }
    }

    internal sealed class RelayRecordingSimulation : IZoneSimulationStep
    {
        private readonly List<string> _log;

        public RelayRecordingSimulation(List<string> log)
        {
            _log = log;
        }

        public void Run(uint currentTick, float deltaTime)
        {
            _log?.Add("Simulation(" + currentTick + ")");
        }
    }

    internal sealed class RelayRecordingOutbound : IZoneOutboundStep
    {
        private readonly List<string> _log;

        public RelayRecordingOutbound(List<string> log)
        {
            _log = log;
        }

        public void Flush(uint currentTick)
        {
            _log?.Add("Outbound(" + currentTick + ")");
        }
    }
}
