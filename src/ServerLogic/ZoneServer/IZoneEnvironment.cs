using System;

namespace IronGrind.ServerLogic.ZoneServer
{
    /// <summary>
    /// Seam over the process environment variables for the zone server composition (ADR-006 Amendment 3: the
    /// database password comes from <c>IRONGRIND_PG_PASSWORD</c>). Tests supply a fake, so no test sets a
    /// process variable. Main thread only.
    /// </summary>
    /// <example>
    /// <code>
    /// string password = environment.GetVariable("IRONGRIND_PG_PASSWORD");
    /// </code>
    /// </example>
    public interface IZoneEnvironment
    {
        /// <summary>Reads one environment variable.</summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The value, or null when the variable is not set.</returns>
        string GetVariable(string name);
    }

    /// <summary>
    /// The real <see cref="IZoneEnvironment"/>: <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// IZoneEnvironment environment = new ProcessZoneEnvironment();
    /// </code>
    /// </example>
    public sealed class ProcessZoneEnvironment : IZoneEnvironment
    {
        /// <inheritdoc/>
        public string GetVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name);
        }
    }
}
