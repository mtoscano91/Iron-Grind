namespace IronGrind.Gateway.Auth;

/// <summary>Outcome category of the login credential path (CR-AUTH-9 steps 3-9). Every credential failure is the same value.</summary>
public enum CredentialResult
{
    /// <summary>The username and password matched, the account is not locked or banned.</summary>
    Passed,

    /// <summary>Unknown user, wrong password, locked or banned; deliberately indistinguishable to the caller.</summary>
    InvalidCredentials,

    /// <summary>Infrastructure failure or a full hash gate; nothing was learned about the credentials.</summary>
    ServerError,
}
