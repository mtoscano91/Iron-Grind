namespace IronGrind.Gateway.Auth;

/// <summary>
/// The <c>MAX_AUTH_PENDING</c> gate around password hashing (ADR-016 Decision 1). One shared instance serves
/// registration (Story 008) and login (Story 009).
/// </summary>
public interface IAuthHashGate
{
    /// <summary>Tries to take a hashing permit without blocking.</summary>
    /// <returns>True when a permit was taken (the caller must call <see cref="Exit"/> once); false when the gate is full.</returns>
    bool TryEnter();

    /// <summary>Returns a permit taken by a successful <see cref="TryEnter"/>. Call exactly once per successful entry.</summary>
    void Exit();
}
