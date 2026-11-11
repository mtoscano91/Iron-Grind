namespace IronGrind.Client.Session
{
    /// <summary>Monotonic clock seam for <see cref="ClientZoneSession"/> (deadlines and heartbeat). Never wall-clock time.</summary>
    public interface IClientSessionClock
    {
        /// <summary>Monotonic seconds since an arbitrary origin; never decreases.</summary>
        double Now { get; }
    }
}
