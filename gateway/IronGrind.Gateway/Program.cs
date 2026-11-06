// IronGrind gateway entry point (ADR-016). Endpoints and the zone link are
// added by the authentication epic's stories; this skeleton only hosts.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.Run();

/// <summary>Entry point type, public so test projects can reference the assembly.</summary>
public partial class Program;
