using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Routing;
using Microsoft.Extensions.Logging;
using Npgsql;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests;

/// <summary>
/// Gateway Story 002 (ADR-016 Decisions 3 and 5, AC-ZI-16): <see cref="NpgsqlZoneIdAllocator"/> against a real
/// PostgreSQL. Uses <see cref="AuthTestDatabase"/> (<c>IRONGRIND_GATEWAY_DB</c> + <c>IRONGRIND_GATEWAY_TEST_ADMIN_DB</c>);
/// ignored without them. <c>zone_id_seq</c> is not reset by the fixture, so the assertions are relative.
/// </summary>
[TestFixture]
[Category("Database")]
internal sealed class Gateway_ZoneIdAllocator_tests
{
    private const int FIRST_RUN_ALLOCATIONS = 100;

    private AuthTestDatabase _db = null!;

    [SetUp]
    public async Task SetUp()
    {
        _db = await AuthTestDatabase.CreateAsync();

        // Idempotent: heals a test database left without the grant by a run killed inside the REVOKE test.
        await _db.AdminExecuteAsync("GRANT USAGE ON SEQUENCE zone_id_seq TO gateway");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_db != null)
        {
            await _db.DisposeAsync();
            _db = null!;
        }
    }

    [Test]
    public async Task test_allocate_100_then_restart_allocates_distinct_increasing_ids()
    {
        var first = new NpgsqlZoneIdAllocator(_db.DataSource, new RecordingLogger<NpgsqlZoneIdAllocator>());
        var ids = new List<uint>();
        for (int i = 0; i < FIRST_RUN_ALLOCATIONS; i++)
        {
            ids.Add((await first.AllocateAsync(CancellationToken.None)).RawValue);
        }

        // Simulated gateway restart: new pool, new allocator instance.
        await _db.RecreateDataSourceAsync();
        var second = new NpgsqlZoneIdAllocator(_db.DataSource, new RecordingLogger<NpgsqlZoneIdAllocator>());
        uint afterRestart = (await second.AllocateAsync(CancellationToken.None)).RawValue;

        Assert.That(ids, Has.None.EqualTo(0u));
        Assert.That(afterRestart, Is.Not.EqualTo(0u));
        Assert.That(ids.Append(afterRestart).Distinct().Count(), Is.EqualTo(FIRST_RUN_ALLOCATIONS + 1));
        Assert.That(afterRestart, Is.GreaterThan(ids.Max()));
    }

    [Test]
    public async Task test_allocate_role_without_usage_throws_and_logs()
    {
        await _db.AdminExecuteAsync("REVOKE USAGE ON SEQUENCE zone_id_seq FROM gateway");
        try
        {
            var logger = new RecordingLogger<NpgsqlZoneIdAllocator>();
            var allocator = new NpgsqlZoneIdAllocator(_db.DataSource, logger);

            PostgresException? ex = Assert.ThrowsAsync<PostgresException>(() => allocator.AllocateAsync(CancellationToken.None));

            Assert.That(ex!.SqlState, Is.EqualTo(PostgresErrorCodes.InsufficientPrivilege));
            Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(e => e.Level == LogLevel.Error));
        }
        finally
        {
            await _db.AdminExecuteAsync("GRANT USAGE ON SEQUENCE zone_id_seq TO gateway");
        }
    }
}

/// <summary>
/// Gateway Story 002: the parts of <see cref="NpgsqlZoneIdAllocator"/> that need no database — the range check
/// and a stopped server. Runs in CI.
/// </summary>
[TestFixture]
internal sealed class Gateway_ZoneIdAllocator_NoDatabase_tests
{
    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase((long)uint.MaxValue + 1)]
    [TestCase(long.MaxValue)]
    public void test_try_to_zone_id_out_of_range_fails(long raw)
    {
        bool ok = NpgsqlZoneIdAllocator.TryToZoneId(raw, out ZoneID zone);

        Assert.That(ok, Is.False);
        Assert.That(zone, Is.EqualTo(ZoneID.Invalid));
    }

    [TestCase(1L, 1u)]
    [TestCase((long)uint.MaxValue, uint.MaxValue)]
    public void test_try_to_zone_id_in_range_converts(long raw, uint expected)
    {
        bool ok = NpgsqlZoneIdAllocator.TryToZoneId(raw, out ZoneID zone);

        Assert.That(ok, Is.True);
        Assert.That(zone.RawValue, Is.EqualTo(expected));
    }

    [Test]
    public async Task test_allocate_stopped_server_throws_and_logs()
    {
        // Port 1 on loopback: nothing listens there, so the connection is refused.
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Username=gateway;Password=unused;Database=unused;Timeout=5;Pooling=false");
        var logger = new RecordingLogger<NpgsqlZoneIdAllocator>();
        var allocator = new NpgsqlZoneIdAllocator(dataSource, logger);

        Assert.That(() => allocator.AllocateAsync(CancellationToken.None), Throws.InstanceOf<Exception>());
        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message, Exception? Exception)>(e => e.Level == LogLevel.Error && e.Exception != null));
    }
}
