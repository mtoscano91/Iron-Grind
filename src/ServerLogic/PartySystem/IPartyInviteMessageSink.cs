using IronGrind.Currency;
using IronGrind.Networking;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// Outbound seam of the party invite flow (Party System Story 007, TR-party-003 / TR-party-004). Both calls go
    /// to the owning client of <c>recipient</c> only. Implementations drop silently when the recipient has no
    /// client. Tick thread only.
    /// </summary>
    public interface IPartyInviteMessageSink
    {
        /// <summary>Delivers the invite banner (also used for the one retry, with the same expiry tick).</summary>
        /// <param name="recipient">The invited character.</param>
        /// <param name="message">The invite.</param>
        void SendInviteReceived(CharacterID recipient, in PartyInviteReceived message);

        /// <summary>Sends a <see cref="PartyInviteResult"/>.</summary>
        /// <param name="recipient">The character that receives the result.</param>
        /// <param name="code">The result code.</param>
        /// <param name="target">
        /// The <c>targetCharacterId</c> field: the invited character when <paramref name="recipient"/> is the
        /// inviter; the inviter when <paramref name="recipient"/> is the invited character.
        /// </param>
        void SendInviteResult(CharacterID recipient, PartyInviteResultCode code, CharacterID target);
    }
}
