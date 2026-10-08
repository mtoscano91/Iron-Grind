using System.Collections.Generic;
using IronGrind.Currency;

namespace IronGrind.LootTableSystem
{
    /// <summary>
    /// Consumer-side interface standing in for the Party System (design/gdd/party-system.md).
    /// The GDDs call it <c>IPartySystem</c>; the code name follows the <c>I[SystemName]Service</c>
    /// rule of ADR-010. It declares only what the loot stories need so far; later stories add more.
    /// </summary>
    public interface IPartyService
    {
        /// <summary>
        /// Returns the party the character currently belongs to (a solo player is a party of one,
        /// design/gdd/loot-table-system.md CR-LT-3), or <see cref="PartyID.Uninitialized"/> if the
        /// character has no party (a caller bug).
        /// </summary>
        /// <param name="characterId">The character to look up.</param>
        PartyID GetPartyID(CharacterID characterId);

        /// <summary>
        /// Returns the party's actual members in join order, with no empty slots, and an empty list
        /// for an unknown party (design/gdd/loot-table-system.md CR-LT-14: N is the winning party's
        /// size at kill time). The Party System GDD (design/gdd/party-system.md) stores four slots
        /// with empty ones as <c>CharacterID(0)</c> and must confirm that its implementation of
        /// this method omits them. The returned list must be a snapshot: it must not change while
        /// the caller is still using it (the loot service pays gold member by member).
        /// </summary>
        /// <param name="partyId">The party to look up.</param>
        IReadOnlyList<CharacterID> GetPartyMembers(PartyID partyId);

        /// <summary>
        /// Returns the member in the given slot, or <see cref="CharacterID.Invalid"/> for an empty or
        /// out-of-range slot, a negative index, or an unknown party (design/gdd/party-system.md
        /// Interactions table). Never throws for a bad index. Used by the round-robin assignment
        /// (design/gdd/loot-table-system.md CR-LT-6).
        /// </summary>
        /// <remarks>
        /// <paramref name="index"/> is in the same index domain as <see cref="GetRrNextIndex"/>: the
        /// value that call returns must be a valid argument here. The Party System must keep that
        /// domain consistent with the <c>MemberCount</c> it uses for the CR-PS-7 modulo, including
        /// members it keeps in place while Ghost or Disconnected; otherwise the rotation breaks.
        /// </remarks>
        /// <param name="partyId">The party to look up.</param>
        /// <param name="index">The slot index.</param>
        CharacterID GetMemberAtIndex(PartyID partyId, int index);

        /// <summary>
        /// Returns the party's current round-robin cursor (<c>rrNextIndex</c>, a <c>byte</c> in
        /// party-system.md CR-PS-7), in <c>[0, MemberCount - 1]</c>; <c>0</c> for a solo party (its
        /// cursor is 0 and ignored) and for an unknown party. NOT in party-system.md yet: the Party
        /// System must add this call, or a single "next round-robin member" call that replaces it
        /// and <see cref="GetMemberAtIndex"/> (which would also let it skip a member who became
        /// ineligible since the last advance). CR-LT-6 needs the cursor value to pick the assignee.
        /// </summary>
        /// <param name="partyId">The party to look up.</param>
        int GetRrNextIndex(PartyID partyId);

        /// <summary>
        /// Asks the Party System to advance the round-robin cursor. The Party System owns the cursor:
        /// it advances, clamps on size change and skips ineligible slots (party-system.md CR-PS-7).
        /// The loot module never writes the cursor itself.
        /// </summary>
        /// <param name="partyId">The party whose cursor advances.</param>
        void AdvanceRrNextIndex(PartyID partyId);

        /// <summary>
        /// Returns false when the member's party status is Ghost (disconnected for longer than the
        /// 5-second reconnect window, design/gdd/party-system.md CR-PS-11); true otherwise. The
        /// auction uses it to skip a top bidder who has a full bag and cannot be waited for
        /// (design/gdd/loot-table-system.md CR-LT-9.1).
        /// </summary>
        /// <param name="characterId">The party member to check.</param>
        bool IsMemberConnected(CharacterID characterId);
    }
}
