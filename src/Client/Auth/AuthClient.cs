using System;
using System.Text;
using IronGrind.Authentication;

namespace IronGrind.Client.Auth
{
    /// <summary>
    /// Client side of the HTTPS login and registration calls (ADR-016 Decision 2, CR-AUTH-6). Plain C# with
    /// callbacks; the HTTP call and the retry wait go through seams. Never logs the password or token.
    /// </summary>
    public sealed class AuthClient
    {
        private const string LOGIN_PATH = "/v1/login";
        private const string REGISTER_PATH = "/v1/register";
        private const long HTTP_OK = 200;

        private readonly AuthClientConfig _config;
        private readonly IAuthHttpTransport _transport;
        private readonly IAuthRetryScheduler _scheduler;

        /// <summary>Creates a client and validates the config.</summary>
        /// <param name="config">The configuration.</param>
        /// <param name="transport">The HTTP seam.</param>
        /// <param name="scheduler">The retry-wait seam.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">The config is invalid (for example a non-https URL).</exception>
        public AuthClient(AuthClientConfig config, IAuthHttpTransport transport, IAuthRetryScheduler scheduler)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _config.Validate();
        }

        /// <summary>
        /// Logs in. A null or all-zero <paramref name="token"/> is the credential path. A non-zero token
        /// is the token path and the password is sent as <c>""</c>. ServerStarting answers are retried.
        /// </summary>
        /// <param name="username">Account username.</param>
        /// <param name="password">Password, sent as entered (no hashing or trimming).</param>
        /// <param name="token">16-byte session token, or null.</param>
        /// <param name="onDone">Called exactly once with the result.</param>
        /// <param name="onServerStarting">Optional; called before each ServerStarting retry wait.</param>
        /// <exception cref="ArgumentNullException">username, password or onDone is null.</exception>
        /// <exception cref="ArgumentException">token is not 16 bytes.</exception>
        public void Login(string username, string password, byte[] token, Action<LoginResponse> onDone, Action onServerStarting)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (onDone == null) throw new ArgumentNullException(nameof(onDone));

            byte[] tokenField = CopyTokenField(token);
            string sentPassword = IsAllZero(tokenField) ? password : "";
            if (TooLong(username) || TooLong(sentPassword))
            {
                onDone(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials));
                return;
            }

            byte[] body = AuthBodyCodec.Encode(new ConnectionRequest(username, sentPassword, tokenField));
            new LoginAttempt(this, BuildUrl(LOGIN_PATH), body, onDone, onServerStarting).Send();
        }

        /// <summary>Registers a new account with its first character.</summary>
        /// <param name="username">Requested username.</param>
        /// <param name="password">Requested password, sent as entered.</param>
        /// <param name="characterName">Requested character name.</param>
        /// <param name="classType">Requested class type.</param>
        /// <param name="onDone">Called exactly once with the result.</param>
        /// <exception cref="ArgumentNullException">A string or onDone is null.</exception>
        public void Register(string username, string password, string characterName, byte classType, Action<RegistrationResult> onDone)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));
            if (characterName == null) throw new ArgumentNullException(nameof(characterName));
            if (onDone == null) throw new ArgumentNullException(nameof(onDone));

            if (TooLong(username))
            {
                onDone(new RegistrationResult(false, RegistrationError.UsernameTooLong));
                return;
            }

            if (TooLong(password))
            {
                onDone(new RegistrationResult(false, RegistrationError.PasswordTooLong));
                return;
            }

            if (TooLong(characterName))
            {
                onDone(new RegistrationResult(false, RegistrationError.CharacterNameTooLong));
                return;
            }

            byte[] body = AuthBodyCodec.Encode(new RegistrationRequest(username, password, characterName, classType));
            _transport.Post(BuildUrl(REGISTER_PATH), body, _config.LoginHttpTimeoutSeconds, result =>
            {
                if (IsOk200(result) && AuthBodyCodec.TryDecode(result.Body, out RegistrationResult decoded))
                {
                    onDone(decoded);
                    return;
                }

                onDone(new RegistrationResult(false, RegistrationError.ServerError));
            });
        }

        private string BuildUrl(string path)
        {
            return _config.GatewayBaseUrl.TrimEnd('/') + path;
        }

        /// <summary>Null means the credential path (all zeros); otherwise a private copy of the 16-byte token.</summary>
        private static byte[] CopyTokenField(byte[] token)
        {
            if (token == null)
            {
                return new byte[AuthBodyCodec.TOKEN_LENGTH];
            }

            if (token.Length != AuthBodyCodec.TOKEN_LENGTH)
            {
                throw new ArgumentException("Token must be exactly " + AuthBodyCodec.TOKEN_LENGTH + " bytes.", nameof(token));
            }

            return (byte[])token.Clone();
        }

        private static bool IsAllZero(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True for a response that may carry a body: Ok outcome, exactly HTTP 200, non-null body.</summary>
        private static bool IsOk200(AuthHttpResult result)
        {
            return result.Outcome == AuthHttpOutcome.Ok && result.StatusCode == HTTP_OK && result.Body != null;
        }

        private static bool TooLong(string text)
        {
            return Encoding.UTF8.GetByteCount(text) > AuthBodyCodec.MAX_STRING_BYTES;
        }

        /// <summary>
        /// One login call: sends the encoded body, resends the same bytes after each ServerStarting answer
        /// (at most <see cref="AuthClientConfig.ServerStartingMaxRetries"/> times), and reports exactly once.
        /// </summary>
        private sealed class LoginAttempt
        {
            private readonly AuthClient _owner;
            private readonly string _url;
            private readonly byte[] _body;
            private readonly Action<LoginResponse> _onDone;
            private readonly Action _onServerStarting;
            private int _retriesDone;

            public LoginAttempt(AuthClient owner, string url, byte[] body, Action<LoginResponse> onDone, Action onServerStarting)
            {
                _owner = owner;
                _url = url;
                _body = body;
                _onDone = onDone;
                _onServerStarting = onServerStarting;
            }

            public void Send()
            {
                _owner._transport.Post(_url, _body, _owner._config.LoginHttpTimeoutSeconds, OnResult);
            }

            private void OnResult(AuthHttpResult result)
            {
                if (!IsOk200(result) || !AuthBodyCodec.TryDecode(result.Body, out LoginResponse decoded))
                {
                    _onDone(LoginResponse.ForFailure(AuthFailureReason.ServerError));
                    return;
                }

                bool serverStarting = !decoded.Success && decoded.ErrorCode == AuthFailureReason.ServerStarting;
                if (!serverStarting)
                {
                    _onDone(decoded);
                    return;
                }

                if (_retriesDone >= _owner._config.ServerStartingMaxRetries)
                {
                    _onDone(LoginResponse.ForFailure(AuthFailureReason.ServerError));
                    return;
                }

                _retriesDone++;
                _onServerStarting?.Invoke();
                _owner._scheduler.Schedule(_owner._config.ServerStartingRetrySeconds, Send);
            }
        }
    }
}
