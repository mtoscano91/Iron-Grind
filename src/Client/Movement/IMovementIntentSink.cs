using IronGrind.Networking;

namespace IronGrind.Client.Movement
{
    /// <summary>
    /// Seam through which the client hands at most one <see cref="MovementIntentMessage"/> per network tick to the transport
    /// (movement-system.md CR-MOV-10; Movement Story 012). The real send (NGO custom message wrapped in the project
    /// envelope with sequence number and SenderEntityID) comes with a client networking epic; tests use a fake.
    /// Implementations must not allocate on the send path (control manifest, Story 012).
    /// </summary>
    public interface IMovementIntentSink
    {
        /// <summary>Sends one intent. The envelope is added by the transport, not by the producer.</summary>
        /// <param name="message">The encoded intent for one tick.</param>
        void Send(in MovementIntentMessage message);
    }
}
