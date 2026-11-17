using System.Runtime.CompilerServices;

// ADR-012 Decision 5: the test assemblies see internal members of IronGrind.ServerLogic.
// InternalsVisibleTo between production assemblies is forbidden; do not add a production assembly here.
[assembly: InternalsVisibleTo("IronGrind.Foundation.EditModeTests")]

// Test-only grant (Mob Spawning Story 008, user decision 2026-11-16): the PlayMode test assembly reads the spawner's
// internal point state. The rule above forbids grants between production assemblies only.
[assembly: InternalsVisibleTo("IronGrind.PlayModeTests")]
