using IronGrind.Gateway.Auth;

namespace IronGrind.Gateway.Hosting;

/// <summary>
/// Outermost middleware: an aborted request ends quietly (abandoned auth, CR-AUTH-9); any other unhandled exception
/// becomes HTTP 500 with an empty body and one Critical entry under
/// <see cref="AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY"/>. Never logs request data.
/// </summary>
public sealed class InfrastructureErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger _log;

    /// <summary>Creates the middleware.</summary>
    /// <param name="next">The next delegate.</param>
    /// <param name="loggerFactory">Source of the infrastructure logger.</param>
    public InfrastructureErrorMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _log = loggerFactory.CreateLogger(AuthenticationService.INFRASTRUCTURE_LOG_CATEGORY);
    }

    /// <summary>Runs the pipeline and converts failures.</summary>
    /// <param name="context">The request.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client is gone; nothing to answer and nothing to report.
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Unhandled exception while serving a gateway request.");
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            }
        }
    }
}
