namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Read-only lookup from a class type byte to its <see cref="ClassDefinition"/>.
    /// Forward-dependency stand-in for the real Class System epic — see
    /// <see cref="ClassDefinition"/> remarks and <see cref="ClassRegistry"/> for the
    /// caller-supplied mock implementation this codebase's tests use today.
    /// </summary>
    public interface IClassRegistry
    {
        /// <summary>
        /// Returns <see langword="true"/> and writes <paramref name="definition"/> when
        /// <paramref name="classType"/> is a known class. Returns <see langword="false"/>
        /// (and a default <see cref="ClassDefinition"/>) otherwise.
        /// </summary>
        bool TryGetClass(byte classType, out ClassDefinition definition);
    }
}
