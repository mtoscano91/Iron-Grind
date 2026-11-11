using System.Diagnostics;
using IronGrind.Gateway.Launch;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 008 (code review): the real OS seams <see cref="SystemProcessRunner"/> and <see cref="SystemFileWriter"/>.
/// Integration tests: they write to a temp folder and run <c>dotnet</c> (always present where these tests run) or a
/// sleeping process (<c>ping</c> on Windows, <c>sleep</c> elsewhere).
/// </summary>
[TestFixture]
internal sealed class Gateway_LaunchOsSeams_tests
{
    private const string DOTNET = "dotnet";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "irongrind-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_dir, recursive: true);
    }

    // SystemFileWriter

    [Test]
    public void test_file_writer_writes_utf8_without_bom_and_overwrites()
    {
        string path = Path.Combine(_dir, "zone-12.env");
        var writer = new SystemFileWriter();

        writer.WriteAllText(path, "ZONE_TEMPLATE=StartingZone\nZONE_PORT=7801\nEXTRA=longer-first-version\n", SystemdZoneLauncher.ENV_FILE_MODE);
        writer.WriteAllText(path, "ZONE_TEMPLATE=StartingZone\nZONE_PORT=7802\n", SystemdZoneLauncher.ENV_FILE_MODE);

        byte[] bytes = File.ReadAllBytes(path);
        Assert.That(bytes[0], Is.EqualTo((byte)'Z'), "no BOM");
        Assert.That(File.ReadAllText(path), Is.EqualTo("ZONE_TEMPLATE=StartingZone\nZONE_PORT=7802\n"), "truncated on overwrite");
    }

    [Test]
    public void test_file_writer_sets_mode_on_new_and_existing_file()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Unix file modes do not apply on Windows; checked by the Linux CI job.");
            return; // Assert.Ignore throws; the return lets the platform analyzer (CA1416) see the guard.
        }

        string path = Path.Combine(_dir, "zone-12.env");
        File.WriteAllText(path, "old");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite);
        var writer = new SystemFileWriter();

        writer.WriteAllText(path, "new", SystemdZoneLauncher.ENV_FILE_MODE);
        Assert.That(File.GetUnixFileMode(path), Is.EqualTo(SystemdZoneLauncher.ENV_FILE_MODE));

        string fresh = Path.Combine(_dir, "zone-13.env");
        writer.WriteAllText(fresh, "new", SystemdZoneLauncher.ENV_FILE_MODE);
        Assert.That(File.GetUnixFileMode(fresh), Is.EqualTo(SystemdZoneLauncher.ENV_FILE_MODE));
    }

    [Test]
    public void test_file_writer_missing_directory_throws()
    {
        string path = Path.Combine(_dir, "missing", "zone-12.env");

        Assert.Throws<DirectoryNotFoundException>(() => new SystemFileWriter().WriteAllText(path, "x", SystemdZoneLauncher.ENV_FILE_MODE));
    }

    // SystemProcessRunner

    [Test]
    public async Task test_runner_returns_exit_code_zero()
    {
        ProcessRunResult result = await new SystemProcessRunner().RunAsync(DOTNET, new[] { "--version" }, TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
    }

    [Test]
    public async Task test_runner_returns_nonzero_exit_and_stderr()
    {
        string missing = Path.Combine(_dir, "missing.dll");

        ProcessRunResult result = await new SystemProcessRunner().RunAsync(DOTNET, new[] { missing }, TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.StandardError, Is.Not.Empty);
    }

    [Test]
    public void test_runner_timeout_kills_process_and_throws()
    {
        (string file, string[] args) = OperatingSystem.IsWindows()
            ? ("ping", new[] { "-n", "60", "127.0.0.1" })
            : ("sleep", new[] { "60" });
        var watch = Stopwatch.StartNew();

        var ex = Assert.ThrowsAsync<TimeoutException>(() =>
            new SystemProcessRunner().RunAsync(file, args, TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("killed"));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "returned well before the 60 s process would end");
    }

    [Test]
    public void test_runner_cancellation_throws_operation_cancelled()
    {
        (string file, string[] args) = OperatingSystem.IsWindows()
            ? ("ping", new[] { "-n", "60", "127.0.0.1" })
            : ("sleep", new[] { "60" });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        Assert.CatchAsync<OperationCanceledException>(() => new SystemProcessRunner().RunAsync(file, args, TimeSpan.FromSeconds(60), cts.Token));
    }

    [Test]
    public void test_start_info_removes_listed_variables_and_keeps_others()
    {
        const string REMOVED = "IRONGRIND_TEST_REMOVED_VAR";
        const string KEPT = "IRONGRIND_TEST_KEPT_VAR";
        Environment.SetEnvironmentVariable(REMOVED, "secret");
        Environment.SetEnvironmentVariable(KEPT, "zone-value");
        try
        {
            ProcessStartInfo info = SystemProcessRunner.CreateStartInfo("zone.exe", new[] { "-zoneId", "12" }, new[] { REMOVED });

            Assert.That(info.Environment.ContainsKey(REMOVED), Is.False);
            Assert.That(info.Environment[KEPT], Is.EqualTo("zone-value"));
            Assert.That(info.UseShellExecute, Is.False);
            Assert.That(info.CreateNoWindow, Is.True);
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { "-zoneId", "12" }));
        }
        finally
        {
            Environment.SetEnvironmentVariable(REMOVED, null);
            Environment.SetEnvironmentVariable(KEPT, null);
        }
    }

    [Test]
    public void test_runner_start_returns_pid()
    {
        int pid = new SystemProcessRunner().Start(DOTNET, new[] { "--version" }, Array.Empty<string>());

        Assert.That(pid, Is.GreaterThan(0));
    }
}
