using System;
using System.Collections.Generic;
using System.Linq;
using IronGrind.Authentication;
using IronGrind.Client.Auth;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace IronGrind.Tests.EditMode.Client.Auth
{
    /// <summary>
    /// EditMode unit tests for Authentication Story 015 - the client HTTPS auth client: mapping, timeout
    /// (AC-AUTH-32 a), ServerStarting retry, HTTPS-only config, oversized fields, plaintext password and token path.
    /// </summary>
    [TestFixture]
    internal sealed class AuthClient_Tests
    {
        private const string BASE_URL = "https://gateway.example";
        private const string HTTP_URL = "http://gateway.example";
        private const int TIMEOUT_SECONDS = 20;
        private const int RETRY_SECONDS = 5;
        private const int MAX_RETRIES = 8;
        private const int STARTING_THEN_SUCCESS = 3;
        private const long STATUS_OK = 200;
        private const long STATUS_SERVER_ERROR = 500;
        private const string USERNAME = "alice";
        private const string PASSWORD = "hunter22";
        private const string SPACED_PASSWORD = "  pa ss  ";
        private const string SECRET = "secret";
        private const string CHARACTER_NAME = "Aldric";
        private const byte CLASS_TYPE = 1;
        private const int LONG_ASCII_LENGTH = 256;
        private const int EMOJI_COUNT = 72;
        private const string EMOJI = "\U0001F600";
        private const uint CHARACTER_ID = 7u;
        private const uint ZONE_ID = 3u;
        private const ushort ZONE_PORT = 7777;
        private const long STATUS_NO_CONTENT = 204;
        private const int WRONG_TOKEN_LENGTH = 5;
        private const byte MUTATED_TOKEN_BYTE = 99;
        private const string URL_FIELD = "GatewayBaseUrl";
        private const string TIMEOUT_FIELD = "LoginHttpTimeoutSeconds";
        private const string RETRY_FIELD = "ServerStartingRetrySeconds";
        private const string MAX_RETRIES_FIELD = "ServerStartingMaxRetries";

        private static readonly byte[] SessionToken =
            { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        // Not a valid LoginResponse (3 bytes) / RegistrationResult (1 byte).
        private static readonly byte[] GarbageLoginBody = { 9, 9, 9 };
        private static readonly byte[] GarbageRegisterBody = { 1 };

        private sealed class FakeTransport : IAuthHttpTransport
        {
            public readonly List<string> Urls = new List<string>();
            public readonly List<byte[]> Bodies = new List<byte[]>();
            public readonly List<int> Timeouts = new List<int>();
            private readonly Queue<Action<AuthHttpResult>> _pending = new Queue<Action<AuthHttpResult>>();

            public int Count => Urls.Count;

            public void Post(string url, byte[] body, int timeoutSeconds, Action<AuthHttpResult> onDone)
            {
                Urls.Add(url);
                Bodies.Add((byte[])body.Clone());
                Timeouts.Add(timeoutSeconds);
                _pending.Enqueue(onDone);
            }

            public void Reply(AuthHttpResult result)
            {
                _pending.Dequeue()(result);
            }
        }

        private sealed class FakeScheduler : IAuthRetryScheduler
        {
            public readonly List<int> Delays = new List<int>();
            private readonly Queue<Action> _pending = new Queue<Action>();

            public void Schedule(int delaySeconds, Action callback)
            {
                Delays.Add(delaySeconds);
                _pending.Enqueue(callback);
            }

            public void FireNext()
            {
                _pending.Dequeue()();
            }
        }

        private AuthClientConfig _config;
        private FakeTransport _transport;
        private FakeScheduler _scheduler;
        private AuthClient _client;
        private List<LoginResponse> _loginResults;
        private List<RegistrationResult> _registerResults;
        private int _startingCalls;

        [SetUp]
        public void SetUp()
        {
            _config = MakeConfig(BASE_URL, TIMEOUT_SECONDS, RETRY_SECONDS, MAX_RETRIES);
            _transport = new FakeTransport();
            _scheduler = new FakeScheduler();
            _client = new AuthClient(_config, _transport, _scheduler);
            _loginResults = new List<LoginResponse>();
            _registerResults = new List<RegistrationResult>();
            _startingCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_config);
        }

        private static AuthClientConfig MakeConfig(string url, int timeout, int retrySeconds, int maxRetries)
        {
            var config = ScriptableObject.CreateInstance<AuthClientConfig>();
            config.Initialize(url, timeout, retrySeconds, maxRetries);
            return config;
        }

        private static AuthHttpResult Ok(byte[] body)
        {
            return new AuthHttpResult(AuthHttpOutcome.Ok, STATUS_OK, body);
        }

        private static byte[] SuccessBody()
        {
            return AuthBodyCodec.Encode(LoginResponse.ForSuccess(
                SessionToken, new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT));
        }

        private static byte[] FailureBody(AuthFailureReason reason)
        {
            return AuthBodyCodec.Encode(LoginResponse.ForFailure(reason));
        }

        private void StartLogin(byte[] token = null, string username = USERNAME, string password = PASSWORD)
        {
            _client.Login(username, password, token, r => _loginResults.Add(r), () => _startingCalls++);
        }

        private void StartRegister(string username = USERNAME, string password = PASSWORD, string characterName = CHARACTER_NAME)
        {
            _client.Register(username, password, characterName, CLASS_TYPE, r => _registerResults.Add(r));
        }

        private void AssertSingleLoginFailure(AuthFailureReason expected)
        {
            Assert.AreEqual(1, _loginResults.Count);
            Assert.IsFalse(_loginResults[0].Success);
            Assert.AreEqual(expected, _loginResults[0].ErrorCode);
        }

        private void AssertSingleRegisterError(RegistrationError expected)
        {
            Assert.AreEqual(1, _registerResults.Count);
            Assert.IsFalse(_registerResults[0].Success);
            Assert.AreEqual(expected, _registerResults[0].ErrorCode);
        }

        // ------------------------------------------------------------ AC-1 mapping

        [Test]
        public void Login_Http200SuccessBody_ReturnsDecodedSuccess()
        {
            StartLogin();
            _transport.Reply(Ok(SuccessBody()));

            Assert.AreEqual(1, _loginResults.Count);
            Assert.IsTrue(_loginResults[0].Success);
            Assert.AreEqual(SessionToken, _loginResults[0].SessionToken);
            Assert.AreEqual(CHARACTER_ID, _loginResults[0].CharacterId.RawValue);
            Assert.AreEqual(ZONE_ID, _loginResults[0].ZoneId.RawValue);
            Assert.AreEqual(ZONE_PORT, _loginResults[0].ZoneServerPort);
        }

        [Test]
        public void Login_Http200InvalidCredentials_ReturnsInvalidCredentials()
        {
            StartLogin();
            _transport.Reply(Ok(FailureBody(AuthFailureReason.InvalidCredentials)));

            AssertSingleLoginFailure(AuthFailureReason.InvalidCredentials);
        }

        [Test]
        public void Login_Http200GarbageBody_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(Ok(GarbageLoginBody));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_Http200NullBody_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(Ok(null));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_OkOutcomeWithStatus204_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.Ok, STATUS_NO_CONTENT, SuccessBody()));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_OkOutcomeWithNon200Status_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.Ok, STATUS_SERVER_ERROR, SuccessBody()));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_HttpError500_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.HttpError, STATUS_SERVER_ERROR, null));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_TransportError_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.TransportError, 0, null));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_Timeout_ReturnsServerError()
        {
            StartLogin();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.Timeout, 0, null));

            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Register_Http200Success_ReturnsSuccess()
        {
            StartRegister();
            _transport.Reply(Ok(AuthBodyCodec.Encode(new RegistrationResult(true, RegistrationError.UsernameTaken))));

            Assert.AreEqual(1, _registerResults.Count);
            Assert.IsTrue(_registerResults[0].Success);
        }

        [Test]
        public void Register_Http200UsernameTaken_ReturnsUsernameTaken()
        {
            StartRegister();
            _transport.Reply(Ok(AuthBodyCodec.Encode(new RegistrationResult(false, RegistrationError.UsernameTaken))));

            AssertSingleRegisterError(RegistrationError.UsernameTaken);
        }

        [Test]
        public void Register_Http200GarbageBody_ReturnsServerError()
        {
            StartRegister();
            _transport.Reply(Ok(GarbageRegisterBody));

            AssertSingleRegisterError(RegistrationError.ServerError);
        }

        [Test]
        public void Register_Http200NullBodyOr204_ReturnsServerError()
        {
            StartRegister();
            _transport.Reply(Ok(null));
            StartRegister();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.Ok, STATUS_NO_CONTENT,
                AuthBodyCodec.Encode(new RegistrationResult(true, RegistrationError.UsernameTaken))));

            Assert.AreEqual(2, _registerResults.Count);
            Assert.IsTrue(_registerResults.All(r => !r.Success && r.ErrorCode == RegistrationError.ServerError));
        }

        [Test]
        public void Register_TransportError_ReturnsServerError()
        {
            StartRegister();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.TransportError, 0, null));

            AssertSingleRegisterError(RegistrationError.ServerError);
        }

        [Test]
        public void Register_HttpErrorAndTimeout_ReturnServerError()
        {
            StartRegister();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.HttpError, STATUS_SERVER_ERROR, null));
            StartRegister();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.Timeout, 0, null));

            Assert.AreEqual(2, _registerResults.Count);
            Assert.IsTrue(_registerResults.All(r => !r.Success && r.ErrorCode == RegistrationError.ServerError));
        }

        // ------------------------------------------------------------ AC-2 timeout, url, timeout value

        [Test]
        public void Login_SendsConfiguredTimeoutAndLoginUrl()
        {
            StartLogin();

            Assert.AreEqual(1, _transport.Count);
            Assert.AreEqual(BASE_URL + "/v1/login", _transport.Urls[0]);
            Assert.AreEqual(TIMEOUT_SECONDS, _transport.Timeouts[0]);
        }

        [Test]
        public void Register_SendsConfiguredTimeoutAndRegisterUrl()
        {
            StartRegister();

            Assert.AreEqual(BASE_URL + "/v1/register", _transport.Urls[0]);
            Assert.AreEqual(TIMEOUT_SECONDS, _transport.Timeouts[0]);
        }

        [Test]
        public void Login_BaseUrlWithTrailingSlash_NoDoubleSlash()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            _config = MakeConfig(BASE_URL + "/", TIMEOUT_SECONDS, RETRY_SECONDS, MAX_RETRIES);
            _client = new AuthClient(_config, _transport, _scheduler);

            StartLogin();
            StartRegister();

            Assert.AreEqual(BASE_URL + "/v1/login", _transport.Urls[0]);
            Assert.AreEqual(BASE_URL + "/v1/register", _transport.Urls[1]);
        }

        // ------------------------------------------------------------ AC-3 ServerStarting retry

        [Test]
        public void Login_ServerStartingThreeTimesThenSuccess_RetriesWithIdenticalBodies()
        {
            StartLogin();
            for (int i = 0; i < STARTING_THEN_SUCCESS; i++)
            {
                _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
                _scheduler.FireNext();
            }

            _transport.Reply(Ok(SuccessBody()));

            Assert.AreEqual(STARTING_THEN_SUCCESS + 1, _transport.Count);
            for (int i = 1; i < _transport.Count; i++)
            {
                Assert.AreEqual(_transport.Bodies[0], _transport.Bodies[i]);
            }

            Assert.AreEqual(STARTING_THEN_SUCCESS, _startingCalls);
            Assert.AreEqual(STARTING_THEN_SUCCESS, _scheduler.Delays.Count);
            Assert.IsTrue(_scheduler.Delays.All(d => d == RETRY_SECONDS));
            Assert.AreEqual(1, _loginResults.Count);
            Assert.IsTrue(_loginResults[0].Success);
        }

        [Test]
        public void Login_ServerStartingBeyondMaxRetries_ReturnsServerErrorAfterNinthRequest()
        {
            StartLogin();
            for (int i = 0; i < MAX_RETRIES; i++)
            {
                _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
                _scheduler.FireNext();
            }

            Assert.AreEqual(0, _loginResults.Count);
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));

            Assert.AreEqual(MAX_RETRIES + 1, _transport.Count);
            Assert.AreEqual(MAX_RETRIES, _startingCalls);
            Assert.AreEqual(MAX_RETRIES, _scheduler.Delays.Count);
            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_TransportErrorOnRetry_ReturnsSingleServerError()
        {
            StartLogin();
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
            _scheduler.FireNext();
            _transport.Reply(new AuthHttpResult(AuthHttpOutcome.TransportError, 0, null));

            Assert.AreEqual(2, _transport.Count);
            Assert.AreEqual(1, _startingCalls);
            Assert.AreEqual(1, _scheduler.Delays.Count);
            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_GarbageBodyOnRetry_ReturnsSingleServerError()
        {
            StartLogin();
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
            _scheduler.FireNext();
            _transport.Reply(Ok(GarbageLoginBody));

            Assert.AreEqual(2, _transport.Count);
            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_ServerStartingWithZeroMaxRetries_ReturnsServerErrorWithoutRetry()
        {
            UnityEngine.Object.DestroyImmediate(_config);
            _config = MakeConfig(BASE_URL, TIMEOUT_SECONDS, RETRY_SECONDS, 0);
            _client = new AuthClient(_config, _transport, _scheduler);

            StartLogin();
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));

            Assert.AreEqual(1, _transport.Count);
            Assert.AreEqual(0, _startingCalls);
            AssertSingleLoginFailure(AuthFailureReason.ServerError);
        }

        [Test]
        public void Login_NullServerStartingCallback_RetriesWithoutThrowing()
        {
            _client.Login(USERNAME, PASSWORD, null, r => _loginResults.Add(r), null);
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
            _scheduler.FireNext();
            _transport.Reply(Ok(SuccessBody()));

            Assert.AreEqual(1, _loginResults.Count);
            Assert.IsTrue(_loginResults[0].Success);
        }

        // ------------------------------------------------------------ AC-4 HTTPS-only config

        [Test]
        public void Validate_HttpUrl_ThrowsNamingTheUrl()
        {
            var config = MakeConfig(HTTP_URL, TIMEOUT_SECONDS, RETRY_SECONDS, MAX_RETRIES);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
                StringAssert.Contains(HTTP_URL, ex.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Constructor_HttpUrl_ThrowsNamingTheUrl()
        {
            var config = MakeConfig(HTTP_URL, TIMEOUT_SECONDS, RETRY_SECONDS, MAX_RETRIES);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(
                    () => new AuthClient(config, _transport, _scheduler));
                StringAssert.Contains(HTTP_URL, ex.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        // Boundary values: the exact number is the point (empty URL, 0 seconds, -1 retries).
        [TestCase("", TIMEOUT_SECONDS, RETRY_SECONDS, MAX_RETRIES, URL_FIELD)]
        [TestCase(BASE_URL, 0, RETRY_SECONDS, MAX_RETRIES, TIMEOUT_FIELD)]
        [TestCase(BASE_URL, TIMEOUT_SECONDS, 0, MAX_RETRIES, RETRY_FIELD)]
        [TestCase(BASE_URL, TIMEOUT_SECONDS, RETRY_SECONDS, -1, MAX_RETRIES_FIELD)]
        public void Validate_EmptyUrlOrOutOfRangeValue_ThrowsNamingTheField(
            string url, int timeout, int retrySeconds, int maxRetries, string field)
        {
            var config = MakeConfig(url, timeout, retrySeconds, maxRetries);
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate());
                StringAssert.Contains(field, ex.Message);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void Validate_UppercaseHttpsScheme_Accepted()
        {
            var config = MakeConfig("HTTPS://gateway.example", TIMEOUT_SECONDS, RETRY_SECONDS, 0);
            try
            {
                Assert.DoesNotThrow(() => config.Validate());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        // ------------------------------------------------------------ AC-5 no trust-all handler

        [Test]
        public void ClientAssembly_HasNoCertificateHandlerSubclass()
        {
            Type[] offenders = typeof(AuthClient).Assembly.GetTypes()
                .Where(t => typeof(CertificateHandler).IsAssignableFrom(t))
                .ToArray();

            Assert.IsEmpty(offenders);
        }

        [Test]
        public void PlayerSettings_InsecureHttpOption_IsNotAllowed()
        {
            Assert.AreEqual(UnityEditor.InsecureHttpOption.NotAllowed, UnityEditor.PlayerSettings.insecureHttpOption);
        }

        // ------------------------------------------------------------ AC-6 oversized fields

        private static string EmojiPassword()
        {
            return string.Concat(Enumerable.Repeat(EMOJI, EMOJI_COUNT));
        }

        [Test]
        public void Register_PasswordOf72FourByteChars_ReturnsPasswordTooLongWithoutRequest()
        {
            StartRegister(password: EmojiPassword());

            Assert.AreEqual(0, _transport.Count);
            AssertSingleRegisterError(RegistrationError.PasswordTooLong);
        }

        [Test]
        public void Register_Username256Bytes_ReturnsUsernameTooLongWithoutRequest()
        {
            StartRegister(username: new string('a', LONG_ASCII_LENGTH));

            Assert.AreEqual(0, _transport.Count);
            AssertSingleRegisterError(RegistrationError.UsernameTooLong);
        }

        [Test]
        public void Register_CharacterName256Bytes_ReturnsCharacterNameTooLongWithoutRequest()
        {
            StartRegister(characterName: new string('a', LONG_ASCII_LENGTH));

            Assert.AreEqual(0, _transport.Count);
            AssertSingleRegisterError(RegistrationError.CharacterNameTooLong);
        }

        [Test]
        public void Login_LongPassword_ReturnsInvalidCredentialsWithoutRequest()
        {
            StartLogin(password: EmojiPassword());

            Assert.AreEqual(0, _transport.Count);
            AssertSingleLoginFailure(AuthFailureReason.InvalidCredentials);
        }

        [Test]
        public void Login_LongUsername_ReturnsInvalidCredentialsWithoutRequest()
        {
            StartLogin(username: new string('a', LONG_ASCII_LENGTH));

            Assert.AreEqual(0, _transport.Count);
            AssertSingleLoginFailure(AuthFailureReason.InvalidCredentials);
        }

        [Test]
        public void Login_LongPasswordOnTokenPath_IsNotRejected()
        {
            StartLogin(token: SessionToken, password: EmojiPassword());

            Assert.AreEqual(1, _transport.Count);
            Assert.AreEqual(0, _loginResults.Count);
        }

        [Test]
        public void Login_LongUsernameOnTokenPath_ReturnsInvalidCredentialsWithoutRequest()
        {
            StartLogin(token: SessionToken, username: new string('a', LONG_ASCII_LENGTH));

            Assert.AreEqual(0, _transport.Count);
            AssertSingleLoginFailure(AuthFailureReason.InvalidCredentials);
        }

        [Test]
        public void Register_Exactly255ByteFields_AreSent()
        {
            string max = new string('a', AuthBodyCodec.MAX_STRING_BYTES);
            StartRegister(username: max, password: max, characterName: max);

            Assert.AreEqual(1, _transport.Count);
        }

        [Test]
        public void Login_Exactly255ByteFields_AreSent()
        {
            string max = new string('a', AuthBodyCodec.MAX_STRING_BYTES);
            StartLogin(username: max, password: max);

            Assert.AreEqual(1, _transport.Count);
            Assert.IsTrue(AuthBodyCodec.TryDecode(_transport.Bodies[0], out ConnectionRequest sent));
            Assert.AreEqual(max, sent.Password);
        }

        // ------------------------------------------------------------ AC-7 plaintext password and token path

        [Test]
        public void Login_PasswordWithSpaces_SentUnchangedWithZeroToken()
        {
            StartLogin(password: SPACED_PASSWORD);

            Assert.IsTrue(AuthBodyCodec.TryDecode(_transport.Bodies[0], out ConnectionRequest sent));
            Assert.AreEqual(USERNAME, sent.Username);
            Assert.AreEqual(SPACED_PASSWORD, sent.Password);
            Assert.AreEqual(new byte[AuthBodyCodec.TOKEN_LENGTH], sent.TokenField);
        }

        [Test]
        public void Login_NonZeroToken_SendsEmptyPasswordAndToken()
        {
            StartLogin(token: SessionToken, password: SECRET);

            Assert.IsTrue(AuthBodyCodec.TryDecode(_transport.Bodies[0], out ConnectionRequest sent));
            Assert.AreEqual("", sent.Password);
            Assert.AreEqual(SessionToken, sent.TokenField);
        }

        [Test]
        public void Login_ExplicitAllZeroToken_UsesCredentialPath()
        {
            StartLogin(token: new byte[AuthBodyCodec.TOKEN_LENGTH], password: SECRET);

            Assert.IsTrue(AuthBodyCodec.TryDecode(_transport.Bodies[0], out ConnectionRequest sent));
            Assert.AreEqual(SECRET, sent.Password);
            Assert.AreEqual(new byte[AuthBodyCodec.TOKEN_LENGTH], sent.TokenField);
        }

        [Test]
        public void Register_PasswordWithSpaces_SentUnchanged()
        {
            StartRegister(password: SPACED_PASSWORD);

            Assert.IsTrue(AuthBodyCodec.TryDecode(_transport.Bodies[0], out RegistrationRequest sent));
            Assert.AreEqual(SPACED_PASSWORD, sent.Password);
            Assert.AreEqual(CHARACTER_NAME, sent.CharacterName);
            Assert.AreEqual(CLASS_TYPE, sent.ClassType);
        }

        // ------------------------------------------------------------ guards, exactly-once

        [Test]
        public void Login_TokenOfWrongLength_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => StartLogin(token: new byte[WRONG_TOKEN_LENGTH]));
            Assert.AreEqual(0, _transport.Count);
        }

        [Test]
        public void Login_NullArguments_ThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _client.Login(null, PASSWORD, null, _ => { }, null));
            Assert.Throws<ArgumentNullException>(() => _client.Login(USERNAME, null, null, _ => { }, null));
            Assert.Throws<ArgumentNullException>(() => _client.Login(USERNAME, PASSWORD, null, null, null));
        }

        [Test]
        public void Register_NullArguments_ThrowArgumentNullException()
        {
            Action<RegistrationResult> done = _ => { };
            Assert.Throws<ArgumentNullException>(() => _client.Register(null, PASSWORD, CHARACTER_NAME, CLASS_TYPE, done));
            Assert.Throws<ArgumentNullException>(() => _client.Register(USERNAME, null, CHARACTER_NAME, CLASS_TYPE, done));
            Assert.Throws<ArgumentNullException>(() => _client.Register(USERNAME, PASSWORD, null, CLASS_TYPE, done));
            Assert.Throws<ArgumentNullException>(() => _client.Register(USERNAME, PASSWORD, CHARACTER_NAME, CLASS_TYPE, null));
        }

        [Test]
        public void Constructor_NullDependencies_ThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new AuthClient(null, _transport, _scheduler));
            Assert.Throws<ArgumentNullException>(() => new AuthClient(_config, null, _scheduler));
            Assert.Throws<ArgumentNullException>(() => new AuthClient(_config, _transport, null));
        }

        [Test]
        public void Login_CallerMutatesTokenAfterCall_RetryBodyUnchanged()
        {
            byte[] token = (byte[])SessionToken.Clone();
            StartLogin(token: token);
            token[0] = MUTATED_TOKEN_BYTE;
            _transport.Reply(Ok(FailureBody(AuthFailureReason.ServerStarting)));
            _scheduler.FireNext();

            Assert.AreEqual(_transport.Bodies[0], _transport.Bodies[1]);
        }

        // ------------------------------------------------------------ real transport, inactive host

        [Test]
        public void Transport_InactiveHost_PostReportsTransportErrorAtOnce()
        {
            var host = new GameObject("AuthTransportHost");
            host.SetActive(false);
            try
            {
                var transport = host.AddComponent<UnityWebRequestAuthTransport>();
                var results = new List<AuthHttpResult>();

                transport.Post(BASE_URL + "/v1/login", GarbageLoginBody, TIMEOUT_SECONDS, results.Add);

                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(AuthHttpOutcome.TransportError, results[0].Outcome);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Transport_InactiveHost_ScheduleRunsCallbackAtOnce()
        {
            var host = new GameObject("AuthTransportHost");
            host.SetActive(false);
            try
            {
                var scheduler = host.AddComponent<UnityWebRequestAuthTransport>();
                int calls = 0;

                scheduler.Schedule(RETRY_SECONDS, () => calls++);

                Assert.AreEqual(1, calls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Login_InactiveTransportHost_EndsAsSingleServerError()
        {
            var host = new GameObject("AuthTransportHost");
            host.SetActive(false);
            try
            {
                var transport = host.AddComponent<UnityWebRequestAuthTransport>();
                var client = new AuthClient(_config, transport, transport);

                client.Login(USERNAME, PASSWORD, null, r => _loginResults.Add(r), () => _startingCalls++);

                AssertSingleLoginFailure(AuthFailureReason.ServerError);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
