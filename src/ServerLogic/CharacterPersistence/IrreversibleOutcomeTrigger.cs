namespace IronGrind.CharacterPersistence
{
    /// <summary>The kind of irreversible outcome that triggers <see cref="ICharacterPersistence.SaveIrreversibleOutcome"/> (CR-CP-1).</summary>
    public enum IrreversibleOutcomeTrigger : byte
    {
        /// <summary>A level-up was committed.</summary>
        LevelUp = 0,

        /// <summary>An enhancement attempt resolved.</summary>
        EnhancementResult = 1,

        /// <summary>A respec was committed.</summary>
        RespecCommit = 2,

        /// <summary>An item was consumed.</summary>
        ItemConsumption = 3,
    }
}
