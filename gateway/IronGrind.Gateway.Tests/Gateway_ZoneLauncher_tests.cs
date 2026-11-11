using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.Launch;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 008: <see cref="SystemdZoneLauncher"/>, <see cref="ProcessZoneLauncher"/>, <see cref="NoOpZoneLauncher"/>,
/// the <c>ZONE_LAUNCHER</c> choice and its startup validation. The OS calls go through fakes; nothing runs <c>systemctl</c>.
/// </summary>
[TestFixture]
internal sealed class Gateway_ZoneLauncher_tests
{
    private const string TEMPLATE = "StartingZone";
    private const ushort PORT = 7801;
    private const string EXE = @"C:\builds\zone\IronGrind.exe";
    private const string CONFIG = @"C:\builds\zone\zone-server.json";

    private static readonly ZoneID ZONE_12 = new(12u);

    private GwLogProvider _logs = null!;
    private ILoggerFactory _loggerFactory = null!;
    private FakeRunner _runner = null!;
    private FakeFiles _files = null!;

    [SetUp]
    public void SetUp()
    {
        _logs = new GwLogProvider();
        _loggerFactory = LoggerFactory.Create(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        _runner = new FakeRunner();
        _files = new FakeFiles { Order = _runner.Order };
    }

    [TearDown]
    public void TearDown() => _loggerFactory.Dispose();

    private SystemdZoneLauncher Systemd() => new(_runner, _files, _loggerFactory);

    private ProcessZoneLauncher Process() => new(_runner, EXE, CONFIG, _loggerFactory);

    private IReadOnlyList<GwLogEntry> LauncherLogs() => _logs.Entries.Where(e => e.Category.StartsWith("IronGrind.Gateway.Launch", StringComparison.Ordinal)).ToList();

    // AC-1: systemd launcher

    [Test]
    public async Task test_systemd_start_writes_env_file_then_runs_systemctl()
    {
        await Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        Assert.That(_files.Writes, Has.Count.EqualTo(1));
        Assert.That(_files.Writes[0].Path, Is.EqualTo("/run/irongrind/zone-12.env"));
        Assert.That(_files.Writes[0].Content, Is.EqualTo("ZONE_TEMPLATE=StartingZone\nZONE_PORT=7801\n"));
        Assert.That(_runner.Runs, Has.Count.EqualTo(1));
        Assert.That(_runner.Runs[0].FileName, Is.EqualTo("systemctl"));
        Assert.That(_runner.Runs[0].Arguments, Is.EqualTo(new[] { "--no-ask-password", "start", "irongrind-zone@12.service" }));
        Assert.That(_runner.Runs[0].Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
        Assert.That(_runner.Order, Is.EqualTo(new[] { "write", "run" }));
        Assert.That(_runner.Starts, Is.Empty);
    }

    [Test]
    public async Task test_systemd_env_file_mode_is_0640()
    {
        await Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        Assert.That(_files.Writes[0].Mode, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
        Assert.That(Convert.ToString((int)_files.Writes[0].Mode, 8), Is.EqualTo("640"));
    }

    [Test]
    public async Task test_systemd_env_file_has_no_secret_fields()
    {
        await Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        string[] keys = _files.Writes[0].Content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('=')[0]).ToArray();
        Assert.That(keys, Is.EqualTo(new[] { "ZONE_TEMPLATE", "ZONE_PORT" }));
    }

    [Test]
    public void test_systemd_nonzero_exit_throws_with_exit_code_and_stderr()
    {
        _runner.NextResult = new ProcessRunResult(1, "Access denied\n");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("exit code 1"));
        Assert.That(ex.Message, Does.Contain("Access denied"));
        Assert.That(ex.Message, Does.Contain("irongrind-zone@12.service"));
    }

    [Test]
    public void test_systemd_write_failure_throws_and_skips_systemctl()
    {
        _files.Throw = new UnauthorizedAccessException("Access to the path '/run/irongrind/zone-12.env' is denied.");

        var ex = Assert.ThrowsAsync<UnauthorizedAccessException>(() => Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None));

        Assert.That(ex!.Message, Does.Contain("zone-12.env"));
        Assert.That(_runner.Runs, Is.Empty);
    }

    [Test]
    public async Task test_systemd_second_start_overwrites_env_file()
    {
        await Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);
        await Systemd().StartAsync(ZONE_12, TEMPLATE, 7802, CancellationToken.None);

        Assert.That(_files.Writes.Select(w => w.Path).Distinct(), Is.EqualTo(new[] { "/run/irongrind/zone-12.env" }));
        Assert.That(_files.Writes[1].Content, Is.EqualTo("ZONE_TEMPLATE=StartingZone\nZONE_PORT=7802\n"));
    }

    [Test]
    public void test_systemd_cancelled_token_does_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.CatchAsync<OperationCanceledException>(() => Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, cts.Token));

        Assert.That(_files.Writes, Is.Empty);
        Assert.That(_runner.Runs, Is.Empty);
    }

    [TestCase("Starting\nZONE_PORT=1")]
    [TestCase("Starting Zone")]
    [TestCase("a=b")]
    [TestCase("\"quoted\"")]
    [TestCase("")]
    public void test_systemd_unsafe_template_name_throws_before_any_call(string template)
    {
        Assert.ThrowsAsync<ArgumentException>(() => Systemd().StartAsync(ZONE_12, template, PORT, CancellationToken.None));

        Assert.That(_files.Writes, Is.Empty);
        Assert.That(_runner.Runs, Is.Empty);
    }

    [Test]
    public void test_systemd_invalid_zone_or_port_throws()
    {
        Assert.ThrowsAsync<ArgumentException>(() => Systemd().StartAsync(ZoneID.Invalid, TEMPLATE, PORT, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Systemd().StartAsync(ZONE_12, TEMPLATE, 0, CancellationToken.None));
        Assert.That(_files.Writes, Is.Empty);
    }

    [Test]
    public async Task test_systemd_success_logs_one_info_line()
    {
        await Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        IReadOnlyList<GwLogEntry> logs = LauncherLogs();
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Level, Is.EqualTo(LogLevel.Information));
        Assert.That(logs[0].Message, Does.Contain("irongrind-zone@12.service"));
    }

    [Test]
    public void test_systemd_timeout_propagates_after_env_file_written()
    {
        _runner.RunThrow = new TimeoutException("systemctl did not exit within 10 s and was killed");

        Assert.ThrowsAsync<TimeoutException>(() => Systemd().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None));

        Assert.That(_files.Writes, Has.Count.EqualTo(1));
        Assert.That(LauncherLogs(), Is.Empty);
    }

    // AC-2: process and no-op launchers

    [Test]
    public async Task test_process_start_removes_gateway_db_variable_from_child()
    {
        await Process().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        Assert.That(_runner.Starts[0].Removed, Is.EqualTo(new[] { "IRONGRIND_GATEWAY_DB" }));
        Assert.That(_runner.Starts[0].Removed, Does.Not.Contain("IRONGRIND_PG_PASSWORD"));
    }

    [Test]
    public async Task test_process_start_passes_zone_arguments_and_logs_pid()
    {
        _runner.NextPid = 4242;

        await Process().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        Assert.That(_runner.Starts, Has.Count.EqualTo(1));
        Assert.That(_runner.Starts[0].FileName, Is.EqualTo(EXE));
        Assert.That(
            _runner.Starts[0].Arguments,
            Is.EqualTo(new[] { "-zoneId", "12", "-zoneTemplate", "StartingZone", "-port", "7801", "-serverConfig", CONFIG }));
        Assert.That(_runner.Runs, Is.Empty);
        Assert.That(_files.Writes, Is.Empty);
        IReadOnlyList<GwLogEntry> logs = LauncherLogs();
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Message, Does.Contain("4242"));
    }

    [Test]
    public void test_process_runner_failure_propagates()
    {
        _runner.StartThrow = new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified.");

        Assert.Throws<System.ComponentModel.Win32Exception>(() => Process().StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None));
    }

    [Test]
    public void test_process_unsafe_template_name_throws_before_start()
    {
        Assert.Throws<ArgumentException>(() => Process().StartAsync(ZONE_12, "x -serverConfig evil", PORT, CancellationToken.None));
        Assert.That(_runner.Starts, Is.Empty);
    }

    [Test]
    public async Task test_noop_logs_one_line_and_calls_nothing()
    {
        await new NoOpZoneLauncher(_loggerFactory).StartAsync(ZONE_12, TEMPLATE, PORT, CancellationToken.None);

        IReadOnlyList<GwLogEntry> logs = LauncherLogs();
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Message, Does.Contain("12").And.Contain(TEMPLATE).And.Contain("7801"));
        Assert.That(_runner.Runs, Is.Empty);
        Assert.That(_runner.Starts, Is.Empty);
        Assert.That(_files.Writes, Is.Empty);
    }

    // AC-3: choice from config, validated at startup

    [TestCase("systemd", typeof(SystemdZoneLauncher))]
    [TestCase("process", typeof(ProcessZoneLauncher))]
    [TestCase("none", typeof(NoOpZoneLauncher))]
    public void test_factory_creates_configured_launcher(string name, Type expected)
    {
        var options = new GatewayOptions { ZoneLauncher = name, ZoneProcessPath = EXE, ZoneServerConfig = CONFIG };

        Assert.That(ZoneLauncherFactory.Create(options, _runner, _files, _loggerFactory), Is.TypeOf(expected));
    }

    [Test]
    public void test_default_launcher_is_none_and_valid()
    {
        var options = new GatewayOptions();

        Assert.That(options.ZoneLauncher, Is.EqualTo("none"));
        Assert.That(new GatewayOptionsValidator().Validate(null, options).Succeeded, Is.True);
    }

    [TestCase("Systemd")]
    [TestCase("docker")]
    [TestCase("")]
    [TestCase(" none")]
    public void test_bad_launcher_value_fails_validation(string value)
    {
        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, new GatewayOptions { ZoneLauncher = value });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("ZONE_LAUNCHER"));
    }

    [Test]
    public void test_process_launcher_without_paths_fails_validation()
    {
        ValidateOptionsResult result = new GatewayOptionsValidator().Validate(null, new GatewayOptions { ZoneLauncher = "process" });

        Assert.That(result.Failed, Is.True);
        Assert.That(result.FailureMessage, Does.Contain("ZONE_PROCESS_PATH").And.Contain("ZONE_SERVER_CONFIG"));
    }

    [Test]
    public void test_systemd_launcher_needs_no_paths()
    {
        Assert.That(new GatewayOptionsValidator().Validate(null, new GatewayOptions { ZoneLauncher = "systemd" }).Succeeded, Is.True);
    }

    [Test]
    public void test_factory_unknown_value_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ZoneLauncherFactory.Create(new GatewayOptions { ZoneLauncher = "docker" }, _runner, _files, _loggerFactory));
    }

    [Test]
    public void test_host_startup_with_bad_launcher_fails()
    {
        using var host = new GwTestHost();
        host.Settings["ZONE_LAUNCHER"] = "docker";

        Exception? thrown = Assert.Catch(() => host.CreateClient().Dispose());

        Exception? walk = thrown;
        while (walk is not null and not OptionsValidationException)
        {
            walk = walk.InnerException;
        }

        Assert.That(walk, Is.Not.Null, thrown?.ToString());
        Assert.That(walk!.Message, Does.Contain("ZONE_LAUNCHER"));
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<(string FileName, string[] Arguments, TimeSpan Timeout)> Runs { get; } = new();

        public List<(string FileName, string[] Arguments, string[] Removed)> Starts { get; } = new();

        public List<string> Order { get; } = new();

        public ProcessRunResult NextResult { get; set; } = new(0, string.Empty);

        public int NextPid { get; set; } = 1;

        public Exception? StartThrow { get; set; }

        public Exception? RunThrow { get; set; }

        public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            Runs.Add((fileName, arguments.ToArray(), timeout));
            if (RunThrow is not null)
            {
                return Task.FromException<ProcessRunResult>(RunThrow);
            }

            Order.Add("run");
            return Task.FromResult(NextResult);
        }

        public int Start(string fileName, IReadOnlyList<string> arguments, IReadOnlyCollection<string> removeEnvironment)
        {
            if (StartThrow is not null)
            {
                throw StartThrow;
            }

            Starts.Add((fileName, arguments.ToArray(), removeEnvironment.ToArray()));
            return NextPid;
        }
    }

    private sealed class FakeFiles : IFileWriter
    {
        public List<(string Path, string Content, UnixFileMode Mode)> Writes { get; } = new();

        public Exception? Throw { get; set; }

        public List<string>? Order { get; set; }

        public void WriteAllText(string path, string content, UnixFileMode mode)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            Writes.Add((path, content, mode));
            Order?.Add("write");
        }
    }
}
