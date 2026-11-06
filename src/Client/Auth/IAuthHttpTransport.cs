using System;

namespace IronGrind.Client.Auth
{
    /// <summary>How one HTTP request to the gateway ended.</summary>
    public enum AuthHttpOutcome
    {
        /// <summary>A response was received with a success status; <see cref="AuthHttpResult.StatusCode"/> and body are valid.</summary>
        Ok,

        /// <summary>A response was received with an HTTP error status.</summary>
        HttpError,

        /// <summary>The request failed before a usable response (connection, TLS, data processing).</summary>
        TransportError,

        /// <summary>The request exceeded its timeout, when the transport can tell a timeout apart.</summary>
        Timeout
    }

    /// <summary>The result of one <see cref="IAuthHttpTransport.Post"/> call.</summary>
    public readonly struct AuthHttpResult
    {
        /// <summary>How the request ended.</summary>
        public readonly AuthHttpOutcome Outcome;

        /// <summary>HTTP status code, or 0 when no response was received.</summary>
        public readonly long StatusCode;

        /// <summary>Response body bytes; may be null or empty.</summary>
        public readonly byte[] Body;

        /// <summary>Creates a result.</summary>
        /// <param name="outcome">How the request ended.</param>
        /// <param name="statusCode">HTTP status code, or 0.</param>
        /// <param name="body">Response body bytes, or null.</param>
        public AuthHttpResult(AuthHttpOutcome outcome, long statusCode, byte[] body)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            Body = body;
        }
    }

    /// <summary>Seam for the single HTTPS POST the auth client makes. Tests replace it with a fake.</summary>
    public interface IAuthHttpTransport
    {
        /// <summary>Posts a binary body and reports the result exactly once.</summary>
        /// <param name="url">Absolute https URL.</param>
        /// <param name="body">Request body bytes (application/octet-stream).</param>
        /// <param name="timeoutSeconds">Request timeout in whole seconds.</param>
        /// <param name="onDone">Called once with the result.</param>
        void Post(string url, byte[] body, int timeoutSeconds, Action<AuthHttpResult> onDone);
    }
}
