namespace IronGrind.Gateway.Auth;

/// <summary>
/// <see cref="IAuthHashGate"/> backed by a <see cref="SemaphoreSlim"/> of <c>MAX_AUTH_PENDING</c> permits.
/// Registered as a singleton in Story 012 (<c>new AuthHashGate(GatewayOptions.MaxAuthPending)</c>).
/// </summary>
public sealed class AuthHashGate : IAuthHashGate, IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    /// <summary>Creates a gate with <paramref name="maxAuthPending"/> permits.</summary>
    /// <param name="maxAuthPending">Concurrent hashes allowed; must be greater than 0.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAuthPending"/> is 0 or negative.</exception>
    public AuthHashGate(int maxAuthPending)
    {
        if (maxAuthPending <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAuthPending), maxAuthPending, "MAX_AUTH_PENDING must be greater than 0.");
        }

        _semaphore = new SemaphoreSlim(maxAuthPending, maxAuthPending);
    }

    /// <inheritdoc />
    public bool TryEnter() => _semaphore.Wait(0);

    /// <inheritdoc />
    public void Exit() => _semaphore.Release();

    /// <summary>Releases the semaphore.</summary>
    public void Dispose() => _semaphore.Dispose();
}
