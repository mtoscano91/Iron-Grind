using System;
using UnityEngine;

namespace IronGrind.Client.Auth
{
    /// <summary>
    /// Client auth configuration (ADR-016, auth-wire-messages.md). Data-driven so no literals live in
    /// <see cref="AuthClient"/>. The gateway URL must be https.
    /// </summary>
    [CreateAssetMenu(fileName = "AuthClientConfig", menuName = "IronGrind/Auth Client Config")]
    public sealed class AuthClientConfig : ScriptableObject
    {
        private const string HTTPS_PREFIX = "https://";

        [SerializeField] private string _gatewayBaseUrl = "";
        [SerializeField] private int _loginHttpTimeoutSeconds = 20;
        [SerializeField] private int _serverStartingRetrySeconds = 5;
        [SerializeField] private int _serverStartingMaxRetries = 8;

        /// <summary>Gateway base URL, e.g. <c>https://host</c>. Must start with <c>https://</c>.</summary>
        public string GatewayBaseUrl => _gatewayBaseUrl;

        /// <summary>LOGIN_HTTP_TIMEOUT_SECONDS: per-request timeout in whole seconds (default 20).</summary>
        public int LoginHttpTimeoutSeconds => _loginHttpTimeoutSeconds;

        /// <summary>LOGIN_SERVER_STARTING_RETRY_SECONDS: wait before resending after ServerStarting (default 5).</summary>
        public int ServerStartingRetrySeconds => _serverStartingRetrySeconds;

        /// <summary>LOGIN_SERVER_STARTING_MAX_RETRIES: resends after ServerStarting before giving up (default 8; 0 = no retry).</summary>
        public int ServerStartingMaxRetries => _serverStartingMaxRetries;

        /// <summary>Sets all values (used by tests and code-built configs).</summary>
        internal void Initialize(string url, int timeout, int retrySeconds, int maxRetries)
        {
            _gatewayBaseUrl = url;
            _loginHttpTimeoutSeconds = timeout;
            _serverStartingRetrySeconds = retrySeconds;
            _serverStartingMaxRetries = maxRetries;
        }

        /// <summary>Checks the configuration.</summary>
        /// <exception cref="InvalidOperationException">The URL is not https, or a value is out of range.</exception>
        public void Validate()
        {
            if (string.IsNullOrEmpty(_gatewayBaseUrl)
                || !_gatewayBaseUrl.StartsWith(HTTPS_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "AuthClientConfig.GatewayBaseUrl must start with https:// (was '" + _gatewayBaseUrl + "').");
            }

            if (_loginHttpTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException(
                    "AuthClientConfig.LoginHttpTimeoutSeconds must be positive (was " + _loginHttpTimeoutSeconds + ").");
            }

            if (_serverStartingRetrySeconds <= 0)
            {
                throw new InvalidOperationException(
                    "AuthClientConfig.ServerStartingRetrySeconds must be positive (was " + _serverStartingRetrySeconds + ").");
            }

            if (_serverStartingMaxRetries < 0)
            {
                throw new InvalidOperationException(
                    "AuthClientConfig.ServerStartingMaxRetries must not be negative (was " + _serverStartingMaxRetries + ").");
            }
        }
    }
}
