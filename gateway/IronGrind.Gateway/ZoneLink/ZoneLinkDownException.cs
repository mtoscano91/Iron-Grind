namespace IronGrind.Gateway.ZoneLink;

/// <summary>
/// A zone request failed because the zone has no live link: either there was none at send time, the write failed, or
/// the link was lost while the request was pending (Gateway Story 004, ADR-016 Decision 3).
/// </summary>
public sealed class ZoneLinkDownException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ZoneLinkDownException()
        : base("The zone has no live link.")
    {
    }

    /// <summary>Creates the exception with a message (never containing a token).</summary>
    /// <param name="message">The message.</param>
    public ZoneLinkDownException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    /// <param name="message">The message (never containing a token).</param>
    /// <param name="innerException">The cause.</param>
    public ZoneLinkDownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
