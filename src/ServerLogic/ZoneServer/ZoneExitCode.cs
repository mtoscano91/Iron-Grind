namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Process exit code of a zone server (ADR-009 Amendment 1 Decision 3). Tasks still incomplete after the
    /// shutdown drain are alerted by the completion queue and never change the code.
    /// </summary>
    /// <example>
    /// <code>
    /// composition.RequestTeardown(ZoneExitCode.Fatal); // the host's Update then starts RunTeardown()
    /// </code>
    /// </example>
    public enum ZoneExitCode
    {
        /// <summary>The zone closed, or a quit request such as SIGTERM arrived.</summary>
        Clean = 0,

        /// <summary>A startup step failed (S-fail).</summary>
        StartupFailed = 1,

        /// <summary>An unhandled failure the host caught at runtime.</summary>
        Fatal = 2
    }
}
