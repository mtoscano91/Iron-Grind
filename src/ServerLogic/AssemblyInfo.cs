using System.Runtime.CompilerServices;

// ADR-012 Decision 5: the EditMode test assembly sees internal members of IronGrind.ServerLogic.
// InternalsVisibleTo between production assemblies is forbidden; do not add another entry here.
[assembly: InternalsVisibleTo("IronGrind.Foundation.EditModeTests")]
