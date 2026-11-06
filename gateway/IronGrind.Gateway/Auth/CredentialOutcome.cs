using IronGrind.CharacterPersistence;

namespace IronGrind.Gateway.Auth;

/// <summary>Result of <see cref="CredentialCheck.RunAsync"/>.</summary>
/// <param name="Result">The outcome category.</param>
/// <param name="AccountID">The authenticated account; set only when <paramref name="Result"/> is <see cref="CredentialResult.Passed"/>, otherwise <c>default</c>.</param>
public readonly record struct CredentialOutcome(CredentialResult Result, AccountID AccountID);
