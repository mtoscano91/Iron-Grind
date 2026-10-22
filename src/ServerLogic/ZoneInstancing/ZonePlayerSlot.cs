using IronGrind.CharacterPersistence;
using IronGrind.CharacterStats;
using IronGrind.Currency;

namespace IronGrind.ServerLogic.ZoneInstancing
{
    /// <summary>
    /// A read-only copy of one occupied player slot (see <see cref="ZoneSlotPool.TryGetPlayer"/>).
    /// </summary>
    public readonly struct ZonePlayerSlot
    {
        /// <summary>Creates a slot snapshot.</summary>
        public ZonePlayerSlot(EntityID entity, CharacterID character, AccountID account, uint clientId, ZoneSessionState state)
        {
            Entity = entity;
            Character = character;
            Account = account;
            ClientId = clientId;
            State = state;
        }

        /// <summary>The EntityID minted for this session.</summary>
        public EntityID Entity { get; }

        /// <summary>The persistent character occupying the slot.</summary>
        public CharacterID Character { get; }

        /// <summary>The owning account.</summary>
        public AccountID Account { get; }

        /// <summary>The connection id of the session.</summary>
        public uint ClientId { get; }

        /// <summary>The session state (every state counts toward capacity).</summary>
        public ZoneSessionState State { get; }
    }
}
