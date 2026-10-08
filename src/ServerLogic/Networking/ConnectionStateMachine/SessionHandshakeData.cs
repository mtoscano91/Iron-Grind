namespace IronGrind.Networking
{
    /// <summary>
    /// The caller-supplied character-state fields <see cref="ConnectionStateMachine.CompleteReAuthSuccess"/>
    /// needs to emit <see cref="INetworkTestObserver.OnSessionHandshakeEmitted"/>, but does not itself
    /// own or compute. <see cref="ConnectionStateMachine"/> owns only session lifecycle state — HP,
    /// MP, XP, level, <c>heldFreePoints</c>, and <c>ClassType</c> belong to Character Stats /
    /// Leveling / Class systems, none of which are wired into this class. <c>characterId</c> and
    /// <c>goldBalance</c> are deliberately excluded from this struct — this class already knows the
    /// former from its own registry (<c>AccountSessionRecord.CharacterId</c>) and computes the latter
    /// itself, fresh, from <c>ICurrencyService.GetBalance</c> post-reconciliation (CR-NET-6.4 step 3).
    /// </summary>
    /// <remarks>
    /// A plain data bundle, not a behavior seam — unlike this class's <see cref="System.Action"/>/
    /// <see cref="System.Func{T,TResult}"/> delegate parameters (see
    /// <see cref="CommitBeforeBroadcastSequencer"/>'s remarks on why behavior seams stay as loose
    /// delegate parameters rather than being bundled). Bundling passive data fields into one struct
    /// here does reduce call-site noise without hiding any ordering-relevant behavior.
    /// </remarks>
    /// <example>
    /// <code>
    /// var handshakeData = new SessionHandshakeData(
    ///     wasKilledWhileDisconnected: false,
    ///     goldVersion: 3u,
    ///     level: 15,
    ///     currentHp: 80,
    ///     currentMp: 40,
    ///     heldFreePoints: 0,
    ///     classType: (byte)ClassType.Warrior);
    /// </code>
    /// </example>
    public readonly struct SessionHandshakeData
    {
        /// <summary>Whether the character's HP reached 0 while disconnected (CR-NET-6.4 step 3).</summary>
        public readonly bool WasKilledWhileDisconnected;

        /// <summary>The <c>GoldSyncEvent.Version</c> seed delivered in the handshake.</summary>
        public readonly uint GoldVersion;

        /// <summary>The character's current level.</summary>
        public readonly int Level;

        /// <summary>The character's current HP.</summary>
        public readonly int CurrentHp;

        /// <summary>The character's current MP.</summary>
        public readonly int CurrentMp;

        /// <summary>The character's currently held (unallocated) free stat points.</summary>
        public readonly int HeldFreePoints;

        /// <summary>The character's class, as the wire-compact byte encoding.</summary>
        public readonly byte ClassType;

        /// <summary>Initializes a new <see cref="SessionHandshakeData"/> with the specified field values.</summary>
        public SessionHandshakeData(bool wasKilledWhileDisconnected, uint goldVersion, int level,
            int currentHp, int currentMp, int heldFreePoints, byte classType)
        {
            WasKilledWhileDisconnected = wasKilledWhileDisconnected;
            GoldVersion = goldVersion;
            Level = level;
            CurrentHp = currentHp;
            CurrentMp = currentMp;
            HeldFreePoints = heldFreePoints;
            ClassType = classType;
        }
    }
}
