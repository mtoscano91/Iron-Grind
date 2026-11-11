using System.Diagnostics;

namespace IronGrind.Gateway.Launch;

/// <summary><see cref="IProcessRunner"/> over <see cref="Process"/>: no shell, no window, arguments as a list.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <inheritdoc/>
    public async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        ProcessStartInfo info = CreateStartInfo(fileName, arguments, Array.Empty<string>());
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;

        using Process process = Process.Start(info) ?? throw new InvalidOperationException($"Process.Start returned null for {fileName}");

        // Read both streams while waiting, so a full pipe cannot block the child. They end when the process exits or is killed.
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        waitCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            await Task.WhenAll(stderr, stdout).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"{fileName} did not exit within {timeout.TotalSeconds:0.###} s and was killed");
        }

        string error = await stderr.ConfigureAwait(false);
        _ = await stdout.ConfigureAwait(false);
        return new ProcessRunResult(process.ExitCode, error);
    }

    /// <inheritdoc/>
    public int Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyCollection<string> removeEnvironment)
    {
        using Process process = Process.Start(CreateStartInfo(fileName, arguments, removeEnvironment))
            ?? throw new InvalidOperationException($"Process.Start returned null for {fileName}");
        return process.Id;
    }

    /// <summary>The start info used by both methods: no shell, no window, arguments as a list, listed variables removed.</summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="arguments">Arguments, one element per argument.</param>
    /// <param name="removeEnvironment">Variables the child must not inherit.</param>
    /// <returns>The start info.</returns>
    public static ProcessStartInfo CreateStartInfo(string fileName, IReadOnlyList<string> arguments, IReadOnlyCollection<string> removeEnvironment)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(removeEnvironment);
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (string name in removeEnvironment)
        {
            info.Environment.Remove(name);
        }

        return info;
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout and the kill.
        }
    }
}
