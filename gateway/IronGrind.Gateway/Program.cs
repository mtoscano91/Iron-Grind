// IronGrind gateway entry point (ADR-016). All wiring lives in GatewayHost so tests can build the same app
// on Kestrel without WebApplicationFactory. The zone-link listener, routing, map, warm spare and launcher are composed there too.

using IronGrind.Gateway.Hosting;

GatewayHost.CreateApp(args).Run();

/// <summary>Entry point type, public so test projects can reference the assembly.</summary>
public partial class Program;
