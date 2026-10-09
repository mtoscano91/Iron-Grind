namespace IronGrind.Networking
{
    /// <summary>
    /// Source of the current server tick (ADR-014 Decision 2). Implemented by <see cref="ServerTickLoop"/>
    /// with the property it already has.
    /// </summary>
    /// <example>
    /// <code>
    /// IServerTickSource source = tickLoop;
    /// uint tick = source.ServerTickNumber;
    /// </code>
    /// </example>
    public interface IServerTickSource
    {
        /// <summary>The current server tick; wraps like any <c>uint</c> tick.</summary>
        uint ServerTickNumber { get; }
    }
}
