namespace IronGrind.Gateway.Launch;

/// <summary>Result of a process that ran to completion through <see cref="IProcessRunner.RunAsync"/>.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardError">Everything the process wrote to stderr (may be empty).</param>
public sealed record ProcessRunResult(int ExitCode, string StandardError);

/// <summary>
/// OS process seam for the zone launchers (Gateway Story 008), so tests assert the exact command line without running
/// <c>systemctl</c> or a zone build. Arguments are passed as a list, never as one shell string.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Runs a process to completion and returns its exit code and stderr.</summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="arguments">Arguments, one element per argument.</param>
    /// <param name="timeout">Longest wait; past it the process tree is killed.</param>
    /// <param name="ct">Cancels the wait; the process tree is killed.</param>
    /// <returns>Exit code and stderr.</returns>
    /// <exception cref="TimeoutException">The process did not exit within <paramref name="timeout"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct);

    /// <summary>Starts a process without waiting for it and without a window.</summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="arguments">Arguments, one element per argument.</param>
    /// <param name="removeEnvironment">Environment variables of this process that the child must not inherit.</param>
    /// <returns>The process id.</returns>
    int Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyCollection<string> removeEnvironment);
}
