using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using IronGrind.Gateway.Auth;
using Microsoft.Extensions.Logging;

namespace IronGrind.Gateway.ZoneLink;

/// <summary>
/// <see cref="IZoneEvictSender"/> backed by <see cref="IZoneLink.EvictAsync"/>. The link is resolved lazily through a
/// factory, which breaks the DI cycle link to map to sender to link.
/// </summary>
public sealed class ZoneLinkEvictSender : IZoneEvictSender
{
    private readonly Func<IZoneLink> _link;
    private readonly ILogger _logger;

    /// <summary>Creates the sender.</summary>
    /// <param name="link">Resolves the zone link when an Evict is sent.</param>
    /// <param name="loggerFactory">Source of the logger.</param>
    public ZoneLinkEvictSender(Func<IZoneLink> link, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _link = link;
        _logger = loggerFactory.CreateLogger<ZoneLinkEvictSender>();
    }

    /// <inheritdoc/>
    public void SendEvict(ZoneID zone, AccountID account)
    {
        Task evict = _link().EvictAsync(zone, account, CancellationToken.None);
        evict.ContinueWith(
            t => _logger.LogWarning(t.Exception?.GetBaseException(), "Evict for account {AccountId} in zone {ZoneId} after registration failed.", account, zone),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
