using System;
using IronGrind.CharacterStats;
using IronGrind.Networking;
using IronGrind.ServerLogic.ZoneInstancing;

namespace IronGrind.ServerLogic.PartySystem
{
    /// <summary>
    /// <see cref="IPartySessionStateSource"/> over the zone slot pool and the session machine (Party System
    /// Story 009, D1): entity to slot <c>Account</c>, then <c>ConnectionStateMachine.TryGetSessionState</c>.
    /// </summary>
    public sealed class SessionMachinePartySessionStateSource : IPartySessionStateSource
    {
        private readonly ZoneSlotPool _pool;
        private readonly ConnectionStateMachine _sessions;

        /// <summary>Creates the source.</summary>
        /// <param name="pool">The zone slot pool.</param>
        /// <param name="sessions">The session machine.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SessionMachinePartySessionStateSource(ZoneSlotPool pool, ConnectionStateMachine sessions)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        /// <inheritdoc/>
        public bool TryIsDisconnected(EntityID entity, out bool disconnected)
        {
            disconnected = false;
            if (!_pool.TryGetPlayer(entity, out ZonePlayerSlot slot)
                || !_sessions.TryGetSessionState(slot.Account.RawValue, out SessionState state))
            {
                return false;
            }

            switch (state)
            {
                case SessionState.Connected:
                    return true;
                case SessionState.Disconnected_SessionActive:
                case SessionState.Reconnecting:
                    disconnected = true;
                    return true;
                default:
                    return false;
            }
        }
    }
}
