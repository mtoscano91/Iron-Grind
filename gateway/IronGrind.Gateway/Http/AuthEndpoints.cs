using IronGrind.Authentication;
using IronGrind.Gateway.Auth;

namespace IronGrind.Gateway.Http;

/// <summary>
/// <c>POST /v1/login</c> and <c>POST /v1/register</c> (ADR-016 Decision 2). Bodies are <c>application/octet-stream</c>;
/// every authentication outcome, failures included, is HTTP 200 with the result in the body. 400, 413, 415 and 500 mean
/// transport or server failure and never reach the authentication service. Request bodies are never logged.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>The only accepted request media type and the response media type.</summary>
    public const string CONTENT_TYPE = "application/octet-stream";

    /// <summary>Largest accepted request body in bytes (the largest valid request is about 600 bytes).</summary>
    public const int MAX_REQUEST_BODY_BYTES = 1024;

    /// <summary>Maps both endpoints.</summary>
    /// <param name="routes">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/login", LoginAsync);
        routes.MapPost("/v1/register", RegisterAsync);
        return routes;
    }

    private delegate bool TryDecode<TRequest>(ReadOnlySpan<byte> body, out TRequest request);

    private static Task LoginAsync(HttpContext context, IAuthenticationService service)
    {
        return HandleAsync<ConnectionRequest, LoginResponse>(
            context, AuthBodyCodec.TryDecode, service.AuthenticateAsync, AuthBodyCodec.Encode);
    }

    private static Task RegisterAsync(HttpContext context, IAuthenticationService service)
    {
        return HandleAsync<RegistrationRequest, RegistrationResult>(
            context, AuthBodyCodec.TryDecode, service.RegisterAsync, AuthBodyCodec.Encode);
    }

    /// <summary>
    /// Shared endpoint flow: read and check the body, decode (400 on failure), call the service with the request-aborted
    /// token, encode the result as a 200. A client abort ends the request with no body and nothing logged (CR-AUTH-9
    /// "Abandoned auth": the services discard the result before any write).
    /// </summary>
    private static async Task HandleAsync<TRequest, TResult>(
        HttpContext context,
        TryDecode<TRequest> tryDecode,
        Func<TRequest, CancellationToken, Task<TResult>> call,
        Func<TResult, byte[]> encode)
    {
        byte[]? body = await ReadBodyAsync(context).ConfigureAwait(false);
        if (body is null)
        {
            return;
        }

        if (!tryDecode(body, out TRequest request))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        try
        {
            TResult result = await call(request, context.RequestAborted).ConfigureAwait(false);
            await WriteBodyAsync(context, encode(result)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client is gone; nothing was written and there is no one to answer.
        }
    }

    /// <summary>
    /// Checks the media type and size, then reads the body. Sets 415 or 413 and returns null on rejection; also returns
    /// null (no status change) when the client aborted while the body was being read.
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(HttpContext context)
    {
        HttpRequest request = context.Request;
        if (!string.Equals(request.ContentType, CONTENT_TYPE, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return null;
        }

        if (request.ContentLength > MAX_REQUEST_BODY_BYTES)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        try
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[MAX_REQUEST_BODY_BYTES + 1];
            int read;
            while ((read = await request.Body.ReadAsync(chunk, context.RequestAborted).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MAX_REQUEST_BODY_BYTES)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Writes a 200 body. <c>Cache-Control: no-store</c>: a login response carries a session token.</summary>
    private static Task WriteBodyAsync(HttpContext context, byte[] body)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = CONTENT_TYPE;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentLength = body.Length;
        return context.Response.Body.WriteAsync(body, context.RequestAborted).AsTask();
    }
}
