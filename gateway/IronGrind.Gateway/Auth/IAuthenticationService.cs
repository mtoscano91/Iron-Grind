using IronGrind.Authentication;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// The gateway's internal authentication service (no longer a cross-process boundary, ADR-016). This story
/// declares <see cref="RegisterAsync"/> only; Stories 009-011 add <c>AuthenticateAsync</c> and <c>GetCharacterIDAsync</c>.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>Validates, hashes and stores a new account with its first character (CR-AUTH-8).</summary>
    /// <param name="request">The decoded registration request.</param>
    /// <param name="ct">Request cancellation; honored before the gate and right after hashing.</param>
    /// <returns>Success, or the failure code. No session token is produced.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the repository call.</exception>
    Task<RegistrationResult> RegisterAsync(RegistrationRequest request, CancellationToken ct);

    /// <summary>
    /// Logs in: a zero <c>TokenField</c> runs the credential check then session establishment (ADR-016 Decision 4).
    /// </summary>
    /// <param name="request">The decoded connection request.</param>
    /// <param name="ct">Request cancellation; read by the credential check only.</param>
    /// <returns>The login response; failures carry <c>InvalidCredentials</c>, <c>ServerStarting</c> or <c>ServerError</c>.</returns>
    /// <exception cref="OperationCanceledException">The request was cancelled during the credential check.</exception>
    /// <exception cref="NotSupportedException">The token path (non-zero token) is not implemented until Story 011.</exception>
    Task<LoginResponse> AuthenticateAsync(ConnectionRequest request, CancellationToken ct);
}
