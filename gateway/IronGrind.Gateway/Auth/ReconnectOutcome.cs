namespace IronGrind.Gateway.Auth;

/// <summary>
/// A zone's answer to <c>Reconnect</c> (ADR-016 Decision 4 token path): the zone validated the presented token and
/// rotated it, or refused it.
/// </summary>
/// <param name="Accepted">True when the zone accepted the token.</param>
/// <param name="NewToken">The rotated 16-byte token when accepted; null when rejected. Never logged.</param>
public readonly record struct ReconnectOutcome(bool Accepted, byte[]? NewToken);
