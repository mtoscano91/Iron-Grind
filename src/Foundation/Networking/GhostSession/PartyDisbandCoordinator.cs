using System;
using System.Collections.Generic;

namespace IronGrind.Networking
{
    /// <summary>
    /// Stateless static coordinator for the EC-GH-7/AC-GH-18 party-disband-mid-ghost-period sequence
    /// (Networking Core Story 020): fires the party-disbanded signal, then stops post-disconnect
    /// party XP share accumulation for every ghosted member of the disbanded party. Mirrors
    /// <see cref="MobDeTargetingCoordinator"/>'s exact stateless-static shape (Story 019) -- this
    /// class owns only sequencing of caller-supplied state and delegate calls, no per-party state of
    /// its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stateless static class, mirroring <see cref="MobDeTargetingCoordinator"/>'s shape, not
    /// <see cref="GhostXpPoolTracker"/>'s:</b> every call is fully described by its arguments --
    /// nothing is tracked between calls. The actual per-character mutation
    /// (<see cref="GhostXpPoolTracker.StopAccumulation"/>) is delegated to the caller-supplied
    /// <see cref="GhostXpPoolTracker"/> instance, not owned here.
    /// </para>
    /// <para>
    /// <b>Mock party-membership provider, per this story's own Implementation Notes:</b> no real
    /// Party System exists yet in this codebase (confirmed -- the GDD's own dependency table lists it
    /// as "not yet authored"). <see cref="ProcessPartyDisband"/>'s
    /// <c>ghostedPartyMemberCharacterIds</c> parameter is supplied by the caller's own test-local mock
    /// party roster, standing in for the real system's membership query -- the same forward-dependency
    /// treatment <see cref="MobDeTargetingCoordinator"/> gives the not-yet-built AI subsystem (see that
    /// class's own remarks). A future Party System story is expected to call this coordinator with a
    /// real membership list.
    /// </para>
    /// <para>
    /// <b>Only ghosted members are passed in -- this coordinator has no opinion on live (non-ghost)
    /// party members:</b> a live member's XP is handled by the (not-yet-built) live party-XP-award
    /// path, which is out of scope for the Ghost Session cluster entirely. The caller is responsible
    /// for filtering its party roster down to only the members currently tracked in the supplied
    /// <see cref="GhostXpPoolTracker"/> before calling this method.
    /// </para>
    /// <para>
    /// <b>Fires <c>OnPartyDisbanded</c> once, before any <see cref="GhostXpPoolTracker.StopAccumulation"/>
    /// call</b> -- the AC-GH-18 ordering this story's Implementation Notes require: the disband tick
    /// is recorded first, then the pool-accumulation-stop guarantee is applied per member, matching
    /// <see cref="MobDeTargetingCoordinator.ProcessTTLExpiry"/>'s own "fire event, then loop" call
    /// order.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // AC-GH-18 / EC-GH-7 -- party disbanded mid-ghost-period.
    /// PartyDisbandCoordinator.ProcessPartyDisband(
    ///     partyId: 900u, tickNumber: 1234u,
    ///     ghostedPartyMemberCharacterIds: new uint[] { 555u },
    ///     xpPoolTracker,
    ///     observer);
    /// </code>
    /// </example>
    public static class PartyDisbandCoordinator
    {
        /// <summary>
        /// Executes the EC-GH-7/AC-GH-18 party-disband sequence: fires
        /// <c>OnPartyDisbanded(partyId, tickNumber)</c>, then calls
        /// <see cref="GhostXpPoolTracker.StopAccumulation"/> for every character ID in
        /// <paramref name="ghostedPartyMemberCharacterIds"/>, in order.
        /// </summary>
        /// <remarks>
        /// Call order (AC-GH-18): <c>OnPartyDisbanded(partyId, tickNumber)</c> -&gt; for each
        /// character ID in <paramref name="ghostedPartyMemberCharacterIds"/>, in order:
        /// <see cref="GhostXpPoolTracker.StopAccumulation"/>.
        /// </remarks>
        /// <param name="partyId">The party being disbanded.</param>
        /// <param name="tickNumber">The server tick the disband event is processed at.</param>
        /// <param name="ghostedPartyMemberCharacterIds">
        /// Every ghosted member's character ID currently tracked in
        /// <paramref name="xpPoolTracker"/>, in the order accumulation should be stopped. Must not be
        /// <see langword="null"/> (an empty list is valid -- no ghosted members in this party).
        /// </param>
        /// <param name="xpPoolTracker">
        /// The XP-pool registry to stop accumulation on. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="observer">Optional test/dev-build observer. Fires <c>OnPartyDisbanded</c>.</param>
        /// <exception cref="ArgumentNullException">
        /// Either <paramref name="ghostedPartyMemberCharacterIds"/> or <paramref name="xpPoolTracker"/>
        /// is <see langword="null"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// PartyDisbandCoordinator.ProcessPartyDisband(
        ///     partyId: 900u, tickNumber: 1234u,
        ///     ghostedPartyMemberCharacterIds: new uint[] { 555u },
        ///     xpPoolTracker,
        ///     observer);
        /// </code>
        /// </example>
        public static void ProcessPartyDisband(
            uint partyId,
            uint tickNumber,
            IReadOnlyList<uint> ghostedPartyMemberCharacterIds,
            GhostXpPoolTracker xpPoolTracker
#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            , INetworkTestObserver observer = null
#endif
            )
        {
            if (ghostedPartyMemberCharacterIds == null)
            {
                throw new ArgumentNullException(nameof(ghostedPartyMemberCharacterIds));
            }

            if (xpPoolTracker == null)
            {
                throw new ArgumentNullException(nameof(xpPoolTracker));
            }

#if UNITY_INCLUDE_TESTS || DEVELOPMENT_BUILD
            observer?.OnPartyDisbanded(partyId, tickNumber);
#endif

            for (int i = 0; i < ghostedPartyMemberCharacterIds.Count; i++)
            {
                xpPoolTracker.StopAccumulation(ghostedPartyMemberCharacterIds[i]);
            }
        }
    }
}
