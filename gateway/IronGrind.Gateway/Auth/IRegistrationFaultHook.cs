namespace IronGrind.Gateway.Auth;

/// <summary>
/// Test seam called between the account insert and the character insert of a registration
/// (GAP-5, AC-AUTH-31). Production uses <see cref="NoRegistrationFaultHook"/>.
/// </summary>
public interface IRegistrationFaultHook
{
    /// <summary>Called inside the registration transaction; a test may throw from it.</summary>
    void BetweenInserts();
}

/// <summary>No-op <see cref="IRegistrationFaultHook"/> for production.</summary>
public sealed class NoRegistrationFaultHook : IRegistrationFaultHook
{
    /// <summary>Shared instance.</summary>
    public static readonly NoRegistrationFaultHook Instance = new();

    private NoRegistrationFaultHook()
    {
    }

    /// <inheritdoc/>
    public void BetweenInserts()
    {
    }
}
