namespace IronGrind.CharacterStats
{
    /// <summary>
    /// Identifies a buff or debuff type. Backed by <see cref="uint"/> to support a large
    /// number of distinct status effects without collision risk.
    /// Concrete values are defined by the Status Effects system (Story 003+).
    /// </summary>
    public enum BuffID : uint
    {
        // Concrete values are assigned by the Status Effects system.
        // This enum is declared here to satisfy Story 001 type dependencies.
    }
}
