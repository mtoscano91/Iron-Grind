using IronGrind.CharacterPersistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IronGrind.Gateway.Routing;

/// <summary>
/// <see cref="IZoneIdAllocator"/> over <c>nextval('zone_id_seq')</c> on the gateway's pool (ADR-016 Decision 5;
/// the <c>gateway</c> role has <c>USAGE</c> on the sequence, migration 004). The sequence is a <c>bigint</c>
/// limited to 1..4294967295 with <c>NO CYCLE</c>; the value is still range-checked before it becomes a ZoneID.
/// </summary>
public sealed class NpgsqlZoneIdAllocator : IZoneIdAllocator
{
    private const string NEXT_ZONE_ID_SQL = "SELECT nextval('zone_id_seq')";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlZoneIdAllocator> _logger;

    /// <summary>Creates the allocator.</summary>
    /// <param name="dataSource">Pooled data source for role gateway.</param>
    /// <param name="logger">Logger for allocation failures.</param>
    public NpgsqlZoneIdAllocator(NpgsqlDataSource dataSource, ILogger<NpgsqlZoneIdAllocator> logger)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ZoneID> AllocateAsync(CancellationToken ct)
    {
        long raw;
        try
        {
            await using NpgsqlCommand command = _dataSource.CreateCommand(NEXT_ZONE_ID_SQL);
            object? result = await command.ExecuteScalarAsync(ct);
            raw = Convert.ToInt64(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZoneID allocation failed with a database or server error");
            throw;
        }

        if (!TryToZoneId(raw, out ZoneID zone))
        {
            _logger.LogError("zone_id_seq returned {Value}, outside 1..{Max}; no ZoneID allocated", raw, uint.MaxValue);
            throw new InvalidOperationException($"zone_id_seq returned {raw}, outside 1..{uint.MaxValue}");
        }

        return zone;
    }

    /// <summary>Converts a sequence value to a ZoneID; fails for 0, negatives and values above <see cref="uint.MaxValue"/>.</summary>
    /// <param name="raw">The value from <c>zone_id_seq</c>.</param>
    /// <param name="zone">The ZoneID, or <see cref="ZoneID.Invalid"/> on failure.</param>
    /// <returns><c>true</c> when <paramref name="raw"/> is in 1..<see cref="uint.MaxValue"/>.</returns>
    public static bool TryToZoneId(long raw, out ZoneID zone)
    {
        if (raw < 1 || raw > uint.MaxValue)
        {
            zone = ZoneID.Invalid;
            return false;
        }

        zone = new ZoneID((uint)raw);
        return true;
    }
}
