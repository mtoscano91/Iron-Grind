using IronGrind.CharacterPersistence;
using IronGrind.Gateway.Config;
using IronGrind.Gateway.ZoneLink;
using IronGrind.ServerLogic.Gateway.Link;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.Auth;

/// <summary>
/// The real account to zone map (Gateway Story 005, ADR-016 Decision 3). Thread-safe: one lock guards the entries, the set
/// of zones with a live link and the link-loss deadlines. Expiry is lazy and deterministic (no timers): every public
/// member first purges the zones whose grace deadline has passed. <see cref="Sweep"/> does the same on demand.
/// </summary>
public sealed class AccountZoneMap : IAccountZoneMap, IZoneLinkEvents
{
    private readonly object _lock = new();
    private readonly Dictionary<AccountID, ZoneID> _entries = new();
    private readonly HashSet<ZoneID> _upZones = new();
    private readonly Dictionary<ZoneID, DateTimeOffset> _deadlines = new();
    private readonly TimeSpan _grace;
    private readonly TimeProvider _time;
    private readonly IZoneEvictSender _evictSender;
    private readonly ILogger _logger;
    private readonly GatewayBootWait _bootWait;

    /// <summary>Creates an empty map with its own boot wait, starting now.</summary>
    /// <param name="options">Source of <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock.</param>
    /// <param name="evictSender">Sends the Evict for a registration conflict.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public AccountZoneMap(GatewayOptions options, TimeProvider time, IZoneEvictSender evictSender, ILoggerFactory loggerFactory)
        : this(options, time, evictSender, loggerFactory, CreateBootWait(options, time))
    {
    }

    /// <summary>Creates an empty map that shares a boot wait with other components (Gateway Story 007).</summary>
    /// <param name="options">Source of <c>GATEWAY_LINK_RETRY_SECONDS</c>.</param>
    /// <param name="time">Clock.</param>
    /// <param name="evictSender">Sends the Evict for a registration conflict.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    /// <param name="bootWait">The shared boot wait.</param>
    public AccountZoneMap(
        GatewayOptions options, TimeProvider time, IZoneEvictSender evictSender, ILoggerFactory loggerFactory, GatewayBootWait bootWait)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _evictSender = evictSender ?? throw new ArgumentNullException(nameof(evictSender));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _bootWait = bootWait ?? throw new ArgumentNullException(nameof(bootWait));
        _logger = loggerFactory.CreateLogger<AccountZoneMap>();
        _grace = TimeSpan.FromSeconds(2.0 * options.LinkRetrySeconds);
    }

    /// <summary>True once <c>GATEWAY_LINK_RETRY_SECONDS x 2</c> have passed since the shared boot wait started (inclusive).</summary>
    public bool IsBootWaitOver => _bootWait.IsOver;

    // Keeps the argument checks of the original constructor (null options or clock throw ArgumentNullException).
    private static GatewayBootWait CreateBootWait(GatewayOptions options, TimeProvider time) => new(options, time);

    /// <inheritdoc/>
    public bool TryGetZone(AccountID account, out ZoneID zone)
    {
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
            if (_entries.TryGetValue(account, out zone))
            {
                return true;
            }
        }

        zone = ZoneID.Invalid;
        return false;
    }

    /// <inheritdoc/>
    public bool IsLinkUp(ZoneID zone)
    {
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
            return _upZones.Contains(zone);
        }
    }

    /// <inheritdoc/>
    public void SetSession(AccountID account, ZoneID zone)
    {
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
            _entries[account] = zone;
        }
    }

    /// <inheritdoc/>
    public bool Remove(AccountID account, ZoneID fromZone)
    {
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
            if (_entries.TryGetValue(account, out ZoneID current) && current.Equals(fromZone))
            {
                _entries.Remove(account);
                return true;
            }

            return false;
        }
    }

    /// <summary>Number of stored entries, read without purging expired ones (tests: shows what only <see cref="Sweep"/> removes).</summary>
    internal int StoredEntryCount
    {
        get
        {
            lock (_lock)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Removes the entries of every zone whose link-loss grace has expired, to free memory.</summary>
    public void Sweep()
    {
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
        }
    }

    /// <inheritdoc/>
    public void OnLinkLost(ZoneID zone)
    {
        lock (_lock)
        {
            DateTimeOffset now = _time.GetUtcNow();
            PurgeLocked(now);
            _upZones.Remove(zone);
            _deadlines[zone] = now + _grace;
        }
    }

    /// <inheritdoc/>
    public void OnZoneRegistered(ZoneID zone, RegisterZoneMessage message)
    {
        List<AccountID>? conflicts = null;
        lock (_lock)
        {
            PurgeLocked(_time.GetUtcNow());
            _deadlines.Remove(zone);
            _upZones.Add(zone);
            RemoveEntriesOfZoneLocked(zone);
            conflicts = AddLiveAccountsLocked(zone, message.LiveAccounts);
        }

        if (conflicts is null)
        {
            return;
        }

        foreach (AccountID account in conflicts)
        {
            try
            {
                _evictSender.SendEvict(zone, account);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sending Evict for account {AccountId} to registering zone {ZoneId} failed.", account, zone);
            }
        }
    }

    /// <inheritdoc/>
    public void OnMessageReceived(ZoneID zone, ZoneToGatewayMessage message)
    {
        if (message.Type == GatewayLinkMessageType.SessionReleased)
        {
            Remove(message.SessionReleased.AccountId, zone);
        }
    }

    // Caller holds _lock. Maps each live account to the zone unless the map assigns it to another zone; returns those
    // conflicting accounts once each (null when there are none).
    private List<AccountID>? AddLiveAccountsLocked(ZoneID zone, AccountID[]? liveAccounts)
    {
        List<AccountID>? conflicts = null;
        HashSet<AccountID>? seen = null;
        foreach (AccountID account in liveAccounts ?? Array.Empty<AccountID>())
        {
            if (!_entries.TryGetValue(account, out ZoneID other))
            {
                _entries[account] = zone;
            }
            else if (!other.Equals(zone) && (seen ??= new HashSet<AccountID>()).Add(account))
            {
                (conflicts ??= new List<AccountID>()).Add(account);
            }
        }

        return conflicts;
    }

    // Caller holds _lock.
    private void PurgeLocked(DateTimeOffset now)
    {
        if (_deadlines.Count == 0)
        {
            return;
        }

        List<ZoneID>? expired = null;
        foreach (KeyValuePair<ZoneID, DateTimeOffset> deadline in _deadlines)
        {
            if (deadline.Value <= now)
            {
                (expired ??= new List<ZoneID>()).Add(deadline.Key);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (ZoneID zone in expired)
        {
            _deadlines.Remove(zone);
            RemoveEntriesOfZoneLocked(zone);
        }
    }

    // Caller holds _lock.
    private void RemoveEntriesOfZoneLocked(ZoneID zone)
    {
        List<AccountID>? doomed = null;
        foreach (KeyValuePair<AccountID, ZoneID> entry in _entries)
        {
            if (entry.Value.Equals(zone))
            {
                (doomed ??= new List<AccountID>()).Add(entry.Key);
            }
        }

        if (doomed is null)
        {
            return;
        }

        foreach (AccountID account in doomed)
        {
            _entries.Remove(account);
        }
    }
}
