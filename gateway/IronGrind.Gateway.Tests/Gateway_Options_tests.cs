using IronGrind.Gateway.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Authentication Story 004: gateway/auth options binding (defaults, environment-style override,
/// array replacement) and validation (GDD safe ranges, joint constraints, ADR-016 knobs).
/// </summary>
[TestFixture]
internal sealed class Gateway_Options_tests
{
    private static readonly int[] DEFAULT_TABLE = { 30, 60, 120, 300, 600 };

    private static IConfiguration Build(params (string Key, string Value)[] pairs)
    {
        var data = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static (GatewayOptions Gateway, AuthOptions Auth) Bind(IConfiguration configuration)
    {
        return (configuration.Get<GatewayOptions>() ?? new GatewayOptions(), configuration.Get<AuthOptions>() ?? new AuthOptions());
    }

    private static void AssertDefaults(GatewayOptions g, AuthOptions a)
    {
        Assert.That(g.HttpPort, Is.EqualTo(8080));
        Assert.That(g.ZoneLinkPort, Is.EqualTo(7900));
        Assert.That(g.ZoneRequestTimeoutMs, Is.EqualTo(2000));
        Assert.That(g.EvictTimeoutMs, Is.EqualTo(11000));
        Assert.That(g.LinkRetrySeconds, Is.EqualTo(2));
        Assert.That(g.MaxAuthPending, Is.EqualTo(10));
        Assert.That(a.LoginFailureLimit, Is.EqualTo(5));
        Assert.That(a.EffectiveLockoutTableSeconds, Is.EqualTo(DEFAULT_TABLE));
        Assert.That(a.Argon2Iterations, Is.EqualTo(2));
        Assert.That(a.Argon2MemorySizeKb, Is.EqualTo(19456));
        Assert.That(a.Argon2Parallelism, Is.EqualTo(1));
        Assert.That(a.UsernameMinLength, Is.EqualTo(3));
        Assert.That(a.UsernameMaxLength, Is.EqualTo(24));
        Assert.That(a.CharacterNameMinLength, Is.EqualTo(3));
        Assert.That(a.CharacterNameMaxLength, Is.EqualTo(16));
        Assert.That(a.PasswordMinLength, Is.EqualTo(8));
        Assert.That(a.PasswordMaxLength, Is.EqualTo(72));
    }

    private static ValidateOptionsResult ValidateAuth(Action<AuthOptions> mutate)
    {
        var options = new AuthOptions();
        mutate(options);
        return new AuthOptionsValidator().Validate(null, options);
    }

    private static ValidateOptionsResult ValidateGateway(Action<GatewayOptions> mutate)
    {
        var options = new GatewayOptions();
        mutate(options);
        return new GatewayOptionsValidator().Validate(null, options);
    }

    private static void AssertFailsNaming(ValidateOptionsResult result, string knob)
    {
        Assert.That(result.Failed, Is.True, knob);
        Assert.That(result.Failures, Has.Some.Contain(knob));
    }

    // ------------------------------------------------------------------ Binding

    [Test]
    public void test_bind_empty_configuration_yields_defaults()
    {
        var (g, a) = Bind(Build());

        AssertDefaults(g, a);
    }

    [Test]
    public void test_bind_real_appsettings_json_yields_defaults()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "gateway.appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();

        var (g, a) = Bind(configuration);

        AssertDefaults(g, a);
        Assert.That(a.LockoutTableSeconds, Is.EqualTo(DEFAULT_TABLE));
    }

    [Test]
    public void test_bind_env_style_override_replaces_scalar_and_array()
    {
        var configuration = Build(
            ("GATEWAY_HTTP_PORT", "9000"),
            ("LOCKOUT_TABLE_SECONDS:0", "20"),
            ("LOCKOUT_TABLE_SECONDS:1", "40"),
            ("LOCKOUT_TABLE_SECONDS:2", "80"),
            ("LOCKOUT_TABLE_SECONDS:3", "400"),
            ("LOCKOUT_TABLE_SECONDS:4", "800"));

        var (g, a) = Bind(configuration);

        Assert.That(g.HttpPort, Is.EqualTo(9000));
        Assert.That(a.EffectiveLockoutTableSeconds, Is.EqualTo(new[] { 20, 40, 80, 400, 800 }));
        Assert.That(a.EffectiveLockoutTableSeconds, Has.Count.EqualTo(5));
    }

    [Test]
    public void test_bind_json_then_env_array_override_replaces_not_appends()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "gateway.appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LOCKOUT_TABLE_SECONDS:0"] = "45",
                ["LOCKOUT_TABLE_SECONDS:1"] = "90",
            })
            .Build();

        var (_, a) = Bind(configuration);

        // Same-index keys override the JSON entries; the config system merges by index, so the
        // override must supply the full table to avoid keeping tail entries.
        Assert.That(a.EffectiveLockoutTableSeconds, Has.Count.EqualTo(5));
        Assert.That(a.EffectiveLockoutTableSeconds[0], Is.EqualTo(45));
        Assert.That(a.EffectiveLockoutTableSeconds[1], Is.EqualTo(90));
    }

    [Test]
    public void test_add_gateway_options_registers_validators_and_binds()
    {
        var services = new ServiceCollection();
        services.AddGatewayOptions(Build(("GATEWAY_HTTP_PORT", "9001")));
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IOptions<GatewayOptions>>().Value.HttpPort, Is.EqualTo(9001));
        Assert.That(provider.GetServices<IValidateOptions<GatewayOptions>>().Single(), Is.TypeOf<GatewayOptionsValidator>());
        Assert.That(provider.GetServices<IValidateOptions<AuthOptions>>().Single(), Is.TypeOf<AuthOptionsValidator>());
    }

    [Test]
    public void test_add_gateway_options_invalid_value_throws_on_access()
    {
        var services = new ServiceCollection();
        services.AddGatewayOptions(Build(("CHARACTER_NAME_MAX_LENGTH", "25")));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<AuthOptions>>().Value);
        Assert.That(ex!.Message, Does.Contain("CHARACTER_NAME_MAX_LENGTH"));
    }

    // --------------------------------------------------------------- Validation

    [Test]
    public void test_validate_defaults_pass()
    {
        Assert.That(new GatewayOptionsValidator().Validate(null, new GatewayOptions()).Succeeded, Is.True);
        Assert.That(new AuthOptionsValidator().Validate(null, new AuthOptions()).Succeeded, Is.True);
    }

    [Test]
    public void test_validate_character_name_max_25_fails_naming_knob()
    {
        AssertFailsNaming(ValidateAuth(o => o.CharacterNameMaxLength = 25), "CHARACTER_NAME_MAX_LENGTH");
    }

    [Test]
    public void test_validate_lockout_table_decreasing_fails_naming_knob()
    {
        AssertFailsNaming(ValidateAuth(o => o.LockoutTableSeconds = new[] { 30, 20, 120, 300, 600 }), "LOCKOUT_TABLE_SECONDS");
    }

    [Test]
    public void test_validate_lockout_table_empty_or_out_of_range_ends_fail()
    {
        AssertFailsNaming(ValidateAuth(o => o.LockoutTableSeconds = Array.Empty<int>()), "LOCKOUT_TABLE_SECONDS");
        AssertFailsNaming(ValidateAuth(o => o.LockoutTableSeconds = new[] { 10, 300 }), "LOCKOUT_TABLE_SECONDS");
        AssertFailsNaming(ValidateAuth(o => o.LockoutTableSeconds = new[] { 30, 200 }), "LOCKOUT_TABLE_SECONDS");
        Assert.That(ValidateAuth(o => o.LockoutTableSeconds = new[] { 15, 300, 300, 3600 }).Succeeded, Is.True);
    }

    [Test]
    public void test_validate_argon2_iterations_1_fails_naming_knob()
    {
        AssertFailsNaming(ValidateAuth(o => o.Argon2Iterations = 1), "ARGON2_ITERATIONS");
    }

    [Test]
    public void test_validate_argon2_memory_and_parallelism_out_of_range_fail()
    {
        AssertFailsNaming(ValidateAuth(o => o.Argon2MemorySizeKb = 19455), "ARGON2_MEMORY_SIZE_KB");
        AssertFailsNaming(ValidateAuth(o => o.Argon2Parallelism = 3), "ARGON2_PARALLELISM");
    }

    [Test]
    public void test_validate_username_max_boundary_32_passes_33_fails()
    {
        Assert.That(ValidateAuth(o => o.UsernameMaxLength = 32).Succeeded, Is.True);
        AssertFailsNaming(ValidateAuth(o => o.UsernameMaxLength = 33), "USERNAME_MAX_LENGTH");
    }

    [Test]
    public void test_validate_min_not_below_max_fails()
    {
        var result = ValidateAuth(o =>
        {
            o.CharacterNameMinLength = 4;
            o.CharacterNameMaxLength = 12;
            o.PasswordMinLength = 12;
            o.PasswordMaxLength = 72;
        });
        Assert.That(result.Succeeded, Is.True);

        AssertFailsNaming(ValidateAuth(o => o.UsernameMinLength = 7), "USERNAME_MIN_LENGTH");
    }

    [Test]
    public void test_validate_collects_all_failures()
    {
        var result = ValidateAuth(o =>
        {
            o.LoginFailureLimit = 2;
            o.PasswordMaxLength = 200;
            o.Argon2Iterations = 9;
        });

        Assert.That(result.Failures, Has.Some.Contain("LOGIN_FAILURE_LIMIT"));
        Assert.That(result.Failures, Has.Some.Contain("PASSWORD_MAX_LENGTH"));
        Assert.That(result.Failures, Has.Some.Contain("ARGON2_ITERATIONS"));
    }

    [Test]
    public void test_validate_http_port_zero_fails_naming_knob()
    {
        AssertFailsNaming(ValidateGateway(o => o.HttpPort = 0), "GATEWAY_HTTP_PORT");
        AssertFailsNaming(ValidateGateway(o => o.ZoneLinkPort = 65536), "GATEWAY_ZONE_LINK_PORT");
        Assert.That(ValidateGateway(o => o.HttpPort = 65535).Succeeded, Is.True);
    }

    [Test]
    public void test_validate_equal_ports_fail_naming_both_knobs()
    {
        var result = ValidateGateway(o => o.HttpPort = o.ZoneLinkPort);

        AssertFailsNaming(result, "GATEWAY_HTTP_PORT");
        AssertFailsNaming(result, "GATEWAY_ZONE_LINK_PORT");
    }

    [Test]
    public void test_validate_evict_timeout_not_above_request_timeout_fails()
    {
        AssertFailsNaming(ValidateGateway(o => o.EvictTimeoutMs = o.ZoneRequestTimeoutMs), "GATEWAY_EVICT_TIMEOUT_MS");
        AssertFailsNaming(ValidateGateway(o => o.EvictTimeoutMs = 1000), "GATEWAY_EVICT_TIMEOUT_MS");
    }

    [Test]
    public void test_validate_non_positive_gateway_values_fail_naming_knob()
    {
        AssertFailsNaming(ValidateGateway(o => o.ZoneRequestTimeoutMs = 0), "GATEWAY_ZONE_REQUEST_TIMEOUT_MS");
        AssertFailsNaming(ValidateGateway(o => o.LinkRetrySeconds = 0), "GATEWAY_LINK_RETRY_SECONDS");
        AssertFailsNaming(ValidateGateway(o => o.MaxAuthPending = 0), "MAX_AUTH_PENDING");
        AssertFailsNaming(ValidateGateway(o => o.EvictTimeoutMs = -1), "GATEWAY_EVICT_TIMEOUT_MS");
        AssertFailsNaming(ValidateGateway(o => o.LinkRetrySeconds = -2), "GATEWAY_LINK_RETRY_SECONDS");
        AssertFailsNaming(ValidateGateway(o => o.MaxAuthPending = -10), "MAX_AUTH_PENDING");
    }

    // ------------------------------------------------- Safe-range boundaries (GDD Tuning Knobs)

    private static readonly Dictionary<string, Action<AuthOptions, int>> AUTH_SETTERS = new()
    {
        ["LOGIN_FAILURE_LIMIT"] = (o, v) => o.LoginFailureLimit = v,
        ["ARGON2_ITERATIONS"] = (o, v) => o.Argon2Iterations = v,
        ["ARGON2_MEMORY_SIZE_KB"] = (o, v) => o.Argon2MemorySizeKb = v,
        ["ARGON2_PARALLELISM"] = (o, v) => o.Argon2Parallelism = v,
        ["USERNAME_MIN_LENGTH"] = (o, v) => o.UsernameMinLength = v,
        ["USERNAME_MAX_LENGTH"] = (o, v) => o.UsernameMaxLength = v,
        ["CHARACTER_NAME_MIN_LENGTH"] = (o, v) => o.CharacterNameMinLength = v,
        ["CHARACTER_NAME_MAX_LENGTH"] = (o, v) => o.CharacterNameMaxLength = v,
        ["PASSWORD_MIN_LENGTH"] = (o, v) => o.PasswordMinLength = v,
        ["PASSWORD_MAX_LENGTH"] = (o, v) => o.PasswordMaxLength = v,
    };

    // Each knob at its GDD safe-range bounds (pass) and one step outside (fail).
    [TestCase("LOGIN_FAILURE_LIMIT", 2, false)]
    [TestCase("LOGIN_FAILURE_LIMIT", 3, true)]
    [TestCase("LOGIN_FAILURE_LIMIT", 10, true)]
    [TestCase("LOGIN_FAILURE_LIMIT", 11, false)]
    [TestCase("ARGON2_ITERATIONS", 4, true)]
    [TestCase("ARGON2_ITERATIONS", 5, false)]
    [TestCase("ARGON2_MEMORY_SIZE_KB", 19456, true)]
    [TestCase("ARGON2_MEMORY_SIZE_KB", 65536, true)]
    [TestCase("ARGON2_MEMORY_SIZE_KB", 65537, false)]
    [TestCase("ARGON2_PARALLELISM", 0, false)]
    [TestCase("ARGON2_PARALLELISM", 2, true)]
    [TestCase("USERNAME_MIN_LENGTH", 1, false)]
    [TestCase("USERNAME_MIN_LENGTH", 2, true)]
    [TestCase("USERNAME_MIN_LENGTH", 6, true)]
    [TestCase("USERNAME_MIN_LENGTH", 7, false)]
    [TestCase("USERNAME_MAX_LENGTH", 11, false)]
    [TestCase("USERNAME_MAX_LENGTH", 12, true)]
    [TestCase("CHARACTER_NAME_MIN_LENGTH", 1, false)]
    [TestCase("CHARACTER_NAME_MIN_LENGTH", 5, false)]
    [TestCase("CHARACTER_NAME_MAX_LENGTH", 11, false)]
    [TestCase("CHARACTER_NAME_MAX_LENGTH", 24, true)]
    [TestCase("PASSWORD_MIN_LENGTH", 5, false)]
    [TestCase("PASSWORD_MIN_LENGTH", 13, false)]
    [TestCase("PASSWORD_MAX_LENGTH", 71, false)]
    [TestCase("PASSWORD_MAX_LENGTH", 72, true)]
    [TestCase("PASSWORD_MAX_LENGTH", 128, true)]
    [TestCase("PASSWORD_MAX_LENGTH", 129, false)]
    public void test_validate_auth_knob_at_boundary_passes_inside_fails_outside(string key, int value, bool expectedValid)
    {
        var result = ValidateAuth(o => AUTH_SETTERS[key](o, value));

        if (expectedValid)
        {
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Failures ?? Array.Empty<string>()));
        }
        else
        {
            AssertFailsNaming(result, key);
        }
    }

    // Lockout first entry [15, 120] and last entry [300, 3600], checked independently.
    [TestCase(new[] { 14, 60, 120, 300, 600 }, false)]
    [TestCase(new[] { 120, 120, 120, 300, 600 }, true)]
    [TestCase(new[] { 121, 121, 121, 300, 600 }, false)]
    [TestCase(new[] { 30, 60, 120, 300, 299 }, false)]
    [TestCase(new[] { 30, 60, 120, 299, 299 }, false)]
    [TestCase(new[] { 30, 60, 120, 300, 3600 }, true)]
    [TestCase(new[] { 30, 60, 120, 300, 3601 }, false)]
    public void test_validate_lockout_table_ends_at_boundary(int[] table, bool expectedValid)
    {
        var result = ValidateAuth(o => o.LockoutTableSeconds = table);

        if (expectedValid)
        {
            Assert.That(result.Succeeded, Is.True, string.Join("; ", result.Failures ?? Array.Empty<string>()));
        }
        else
        {
            AssertFailsNaming(result, "LOCKOUT_TABLE_SECONDS");
        }
    }
}
