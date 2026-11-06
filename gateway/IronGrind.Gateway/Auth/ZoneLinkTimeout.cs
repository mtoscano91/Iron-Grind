namespace IronGrind.Gateway.Auth;

/// <summary>Shared timeout wrapper for zone link calls (used by session establishment and the token path).</summary>
internal static class ZoneLinkTimeout
{
    /// <summary>
    /// Runs one link call with its own token source (the timeout is the only token the link sees) and also bounds
    /// the wait, so a link that ignores the token cannot hang the caller. A timeout, whether the link honours the
    /// token or not, surfaces as a <see cref="TimeoutException"/> naming <paramref name="step"/> (never a token).
    /// </summary>
    /// <typeparam name="T">The call's result type.</typeparam>
    /// <param name="call">The link call; receives the timeout token.</param>
    /// <param name="timeoutMs">The timeout in milliseconds.</param>
    /// <param name="step">Name of the step for the exception message.</param>
    /// <returns>The call's result.</returns>
    /// <exception cref="TimeoutException">The call did not finish within <paramref name="timeoutMs"/>.</exception>
    public static async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> call, int timeoutMs, string step)
    {
        using CancellationTokenSource cts = new(timeoutMs);
        try
        {
            return await call(cts.Token).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && cts.IsCancellationRequested))
        {
            throw new TimeoutException($"{step} timed out after {timeoutMs} ms.", ex);
        }
    }
}
