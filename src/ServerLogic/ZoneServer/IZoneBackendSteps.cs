namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// The backend half of startup (ADR-009 Amendment 1 Decision 2): S3 random, S4 database, S5 object graph,
    /// S7 relay listener. Story 043 calls it in that order; Story 044 supplies the real implementation without
    /// changing the order. Each method may throw or return <c>false</c>; either one is S-fail. Internal because
    /// nothing outside the server assembly needs it. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!backend.InitializeRandom()) { /* S-fail at S3 */ }
    /// </code>
    /// </example>
    internal interface IZoneBackendSteps
    {
        /// <summary>S3: creates the one random provider.</summary>
        /// <returns>True on success.</returns>
        bool InitializeRandom();

        /// <summary>S4: builds the data source and runs the warm-up. Never logs the connection string.</summary>
        /// <returns>True on success.</returns>
        bool OpenDatabase();

        /// <summary>S5: constructs the object graph (no I/O) and attaches it to the composition.</summary>
        /// <returns>True on success.</returns>
        bool ConstructObjects();

        /// <summary>S7: starts the relay listener.</summary>
        /// <returns>True on success.</returns>
        bool StartListener();
    }
}
