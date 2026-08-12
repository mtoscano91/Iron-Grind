using System.Collections.Generic;

namespace IronGrind.LevelingSystem
{
    /// <summary>
    /// Caller-supplied in-memory <see cref="IClassRegistry"/> implementation. Mirrors the
    /// same "test-local/caller-supplied state for not-yet-built subsystems" idiom as
    /// <see cref="LevelingService.RegisterPlayerEntity"/> (this epic) and
    /// <c>PartyDisbandCoordinator</c> (Networking Core Story 020) — nothing here is the real
    /// Class System, it is a stand-in a caller populates with whatever class data it needs.
    /// </summary>
    public sealed class ClassRegistry : IClassRegistry
    {
        private readonly Dictionary<byte, ClassDefinition> _classes = new Dictionary<byte, ClassDefinition>();

        /// <summary>Registers or overwrites the <see cref="ClassDefinition"/> for <paramref name="classType"/>.</summary>
        public void RegisterClass(byte classType, ClassDefinition definition)
        {
            _classes[classType] = definition;
        }

        /// <inheritdoc/>
        public bool TryGetClass(byte classType, out ClassDefinition definition)
            => _classes.TryGetValue(classType, out definition);
    }
}
