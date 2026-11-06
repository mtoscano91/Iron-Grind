using System;
using System.Reflection;
using IronGrind.Authentication;
using IronGrind.CharacterPersistence;
using IronGrind.Currency;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests
{
    /// <summary>
    /// Authentication Story 002: auth body codec (round-trip, golden bytes, malformed input, limits).
    /// </summary>
    [TestFixture]
    internal sealed class Authentication_BodyCodec_Tests
    {
        private const string USERNAME = "alice_0";
        private const string PASSWORD = "pässwörd";
        private const string CHARACTER_NAME = "Grim_Iron7";
        private const byte CLASS_TYPE = 3;
        private const uint CHARACTER_ID = 77;
        private const uint ZONE_ID = 5;
        private const ushort ZONE_PORT = 7777;
        private const int TOKEN_LENGTH = 16;
        private const int SUCCESS_LENGTH = 28;
        private const int FAILURE_LENGTH = 2;
        private const int MAX_STRING_BYTES = 255;
        private const string EMOJI = "\U0001F600";
        private const int EMOJI_COUNT_OVER_LIMIT = 64;

        // --------------------------------------------------------------- Fixtures

        private static byte[] MakeToken()
        {
            var token = new byte[TOKEN_LENGTH];
            for (int i = 0; i < TOKEN_LENGTH; i++)
            {
                token[i] = (byte)(i + 1);
            }

            return token;
        }

        private static ConnectionRequest MakeConnectionRequest()
        {
            return new ConnectionRequest(USERNAME, PASSWORD, MakeToken());
        }

        private static RegistrationRequest MakeRegistrationRequest()
        {
            return new RegistrationRequest(USERNAME, PASSWORD, CHARACTER_NAME, CLASS_TYPE);
        }

        private static LoginResponse MakeLoginSuccess()
        {
            return LoginResponse.ForSuccess(MakeToken(), new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT);
        }

        private static void AssertAllTruncationsFail(byte[] body, Func<byte[], bool> tryDecode)
        {
            for (int length = 0; length < body.Length; length++)
            {
                var truncated = new byte[length];
                Array.Copy(body, truncated, length);
                Assert.That(tryDecode(truncated), Is.False, "truncated to " + length);
            }
        }

        private static byte[] WithTrailingByte(byte[] body)
        {
            var longer = new byte[body.Length + 1];
            Array.Copy(body, longer, body.Length);
            return longer;
        }

        // --------------------------------------------------------------- AC-1 round-trip

        [Test]
        public void test_connection_request_round_trip_all_fields_equal()
        {
            byte[] body = AuthBodyCodec.Encode(MakeConnectionRequest());

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Username, Is.EqualTo(USERNAME));
            Assert.That(decoded.Password, Is.EqualTo(PASSWORD));
            Assert.That(decoded.TokenField, Is.EqualTo(MakeToken()));
        }

        [Test]
        public void test_connection_request_empty_password_zero_token_round_trip()
        {
            var request = new ConnectionRequest(USERNAME, string.Empty, new byte[TOKEN_LENGTH]);

            Assert.That(AuthBodyCodec.TryDecode(AuthBodyCodec.Encode(request), out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Password, Is.EqualTo(string.Empty));
            Assert.That(decoded.TokenField, Is.EqualTo(new byte[TOKEN_LENGTH]));
        }

        [Test]
        public void test_registration_request_round_trip_all_fields_equal()
        {
            byte[] body = AuthBodyCodec.Encode(MakeRegistrationRequest());

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationRequest decoded), Is.True);
            Assert.That(decoded.Username, Is.EqualTo(USERNAME));
            Assert.That(decoded.Password, Is.EqualTo(PASSWORD));
            Assert.That(decoded.CharacterName, Is.EqualTo(CHARACTER_NAME));
            Assert.That(decoded.ClassType, Is.EqualTo(CLASS_TYPE));
        }

        [Test]
        public void test_login_response_success_round_trip_all_fields_equal()
        {
            byte[] body = AuthBodyCodec.Encode(MakeLoginSuccess());

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse decoded), Is.True);
            Assert.That(decoded.Success, Is.True);
            Assert.That(decoded.ErrorCode, Is.EqualTo(AuthFailureReason.None));
            Assert.That(decoded.SessionToken, Is.EqualTo(MakeToken()));
            Assert.That(decoded.CharacterId, Is.EqualTo(new CharacterID(CHARACTER_ID)));
            Assert.That(decoded.ZoneId, Is.EqualTo(new ZoneID(ZONE_ID)));
            Assert.That(decoded.ZoneServerPort, Is.EqualTo(ZONE_PORT));
        }

        [Test]
        public void test_login_response_failure_round_trip_reason_preserved()
        {
            byte[] body = AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.ServerStarting));

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse decoded), Is.True);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.ErrorCode, Is.EqualTo(AuthFailureReason.ServerStarting));
        }

        [Test]
        public void test_registration_result_failure_round_trip_error_preserved()
        {
            byte[] body = AuthBodyCodec.Encode(new RegistrationResult(false, RegistrationError.CharacterNameMatchesUsername));

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationResult decoded), Is.True);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.ErrorCode, Is.EqualTo(RegistrationError.CharacterNameMatchesUsername));
        }

        [Test]
        public void test_registration_result_success_round_trip()
        {
            byte[] body = AuthBodyCodec.Encode(new RegistrationResult(true, RegistrationError.UsernameTaken));

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationResult decoded), Is.True);
            Assert.That(decoded.Success, Is.True);
        }

        [Test]
        public void test_decoded_token_is_fresh_copy()
        {
            byte[] body = AuthBodyCodec.Encode(MakeConnectionRequest());
            AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded);

            body[body.Length - 1] = 0xFF;

            Assert.That(decoded.TokenField, Is.EqualTo(MakeToken()));
        }

        // --------------------------------------------------------------- AC-2 no AccountID, fixed lengths

        [Test]
        public void test_login_response_has_no_account_id_member()
        {
            const string ACCOUNT_ID_FULL_NAME = "IronGrind.CharacterPersistence.AccountID";
            const string ACCOUNT_ID_MEMBER = "accountid";
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

            foreach (FieldInfo field in typeof(LoginResponse).GetFields(flags))
            {
                Assert.That(field.Name.ToLowerInvariant(), Is.Not.EqualTo(ACCOUNT_ID_MEMBER));
                Assert.That(field.FieldType.FullName, Is.Not.EqualTo(ACCOUNT_ID_FULL_NAME));
            }

            foreach (PropertyInfo property in typeof(LoginResponse).GetProperties(flags))
            {
                Assert.That(property.Name.ToLowerInvariant(), Is.Not.EqualTo(ACCOUNT_ID_MEMBER));
                Assert.That(property.PropertyType.FullName, Is.Not.EqualTo(ACCOUNT_ID_FULL_NAME));
            }
        }

        [Test]
        public void test_login_response_success_encodes_to_28_bytes()
        {
            Assert.That(AuthBodyCodec.Encode(MakeLoginSuccess()).Length, Is.EqualTo(SUCCESS_LENGTH));
        }

        [Test]
        public void test_login_response_failure_encodes_to_2_bytes()
        {
            Assert.That(AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials)).Length,
                Is.EqualTo(FAILURE_LENGTH));
        }

        // --------------------------------------------------------------- AC-3 golden bytes

        [Test]
        public void test_connection_request_golden_bytes()
        {
            var request = new ConnectionRequest("bob", "pw", MakeToken());
            byte[] expected =
            {
                0x03, 0x00, 0x62, 0x6F, 0x62,
                0x02, 0x00, 0x70, 0x77,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10
            };

            Assert.That(AuthBodyCodec.Encode(request), Is.EqualTo(expected));
        }

        [Test]
        public void test_registration_request_golden_bytes()
        {
            var request = new RegistrationRequest("bob", "pw", "Rex", 2);
            byte[] expected =
            {
                0x03, 0x00, 0x62, 0x6F, 0x62,
                0x02, 0x00, 0x70, 0x77,
                0x03, 0x00, 0x52, 0x65, 0x78,
                0x02
            };

            Assert.That(AuthBodyCodec.Encode(request), Is.EqualTo(expected));
        }

        [Test]
        public void test_login_response_success_golden_bytes()
        {
            byte[] expected =
            {
                0x01, 0x00,
                0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                0x4D, 0x00, 0x00, 0x00,
                0x05, 0x00, 0x00, 0x00,
                0x61, 0x1E
            };

            Assert.That(AuthBodyCodec.Encode(MakeLoginSuccess()), Is.EqualTo(expected));
        }

        [Test]
        public void test_login_response_failure_golden_bytes()
        {
            byte[] expected = { 0x00, 0x01 };

            Assert.That(AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials)),
                Is.EqualTo(expected));
        }

        [Test]
        public void test_registration_result_failure_golden_bytes()
        {
            byte[] expected = { 0x00, 0x07 };

            Assert.That(AuthBodyCodec.Encode(new RegistrationResult(false, RegistrationError.CharacterNameTaken)),
                Is.EqualTo(expected));
        }

        // --------------------------------------------------------------- AC-4 malformed input

        [Test]
        public void test_connection_request_every_truncation_fails()
        {
            AssertAllTruncationsFail(AuthBodyCodec.Encode(MakeConnectionRequest()),
                b => AuthBodyCodec.TryDecode(b, out ConnectionRequest _));
        }

        [Test]
        public void test_registration_request_every_truncation_fails()
        {
            AssertAllTruncationsFail(AuthBodyCodec.Encode(MakeRegistrationRequest()),
                b => AuthBodyCodec.TryDecode(b, out RegistrationRequest _));
        }

        [Test]
        public void test_login_response_success_every_truncation_fails()
        {
            AssertAllTruncationsFail(AuthBodyCodec.Encode(MakeLoginSuccess()),
                b => AuthBodyCodec.TryDecode(b, out LoginResponse _));
        }

        [Test]
        public void test_login_response_failure_every_truncation_fails()
        {
            AssertAllTruncationsFail(AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials)),
                b => AuthBodyCodec.TryDecode(b, out LoginResponse _));
        }

        [Test]
        public void test_registration_result_every_truncation_fails()
        {
            AssertAllTruncationsFail(AuthBodyCodec.Encode(new RegistrationResult(false, RegistrationError.UsernameTaken)),
                b => AuthBodyCodec.TryDecode(b, out RegistrationResult _));
        }

        [Test]
        public void test_connection_request_prefix_ffff_fails()
        {
            byte[] body = AuthBodyCodec.Encode(MakeConnectionRequest());
            body[0] = 0xFF;
            body[1] = 0xFF;

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest _), Is.False);
        }

        [Test]
        public void test_registration_request_prefix_ffff_fails()
        {
            byte[] body = AuthBodyCodec.Encode(MakeRegistrationRequest());
            body[0] = 0xFF;
            body[1] = 0xFF;

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationRequest _), Is.False);
        }

        [Test]
        public void test_connection_request_trailing_byte_fails()
        {
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(MakeConnectionRequest()));

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest _), Is.False);
        }

        [Test]
        public void test_registration_request_trailing_byte_fails()
        {
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(MakeRegistrationRequest()));

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationRequest _), Is.False);
        }

        [Test]
        public void test_login_response_success_trailing_byte_fails()
        {
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(MakeLoginSuccess()));

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse _), Is.False);
        }

        [Test]
        public void test_login_response_failure_trailing_byte_fails()
        {
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.InvalidCredentials)));

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse _), Is.False);
        }

        [Test]
        public void test_registration_result_trailing_byte_fails()
        {
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(new RegistrationResult(true, RegistrationError.UsernameTaken)));

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationResult _), Is.False);
        }

        [Test]
        public void test_login_response_success_flag_2_fails()
        {
            byte[] body = AuthBodyCodec.Encode(MakeLoginSuccess());
            body[0] = 2;

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse _), Is.False);
        }

        [Test]
        public void test_registration_result_success_flag_2_fails()
        {
            byte[] body = AuthBodyCodec.Encode(new RegistrationResult(true, RegistrationError.UsernameTaken));
            body[0] = 2;

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationResult _), Is.False);
        }

        // --------------------------------------------------------------- AC-5 token length

        [Test]
        public void test_connection_request_token_15_bytes_throws()
        {
            var request = new ConnectionRequest(USERNAME, PASSWORD, new byte[TOKEN_LENGTH - 1]);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_connection_request_token_17_bytes_throws()
        {
            var request = new ConnectionRequest(USERNAME, PASSWORD, new byte[TOKEN_LENGTH + 1]);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_connection_request_null_token_throws()
        {
            var request = new ConnectionRequest(USERNAME, PASSWORD, null);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_login_response_success_wrong_token_length_throws()
        {
            var response = LoginResponse.ForSuccess(new byte[TOKEN_LENGTH - 1], new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(response));
        }

        // --------------------------------------------------------------- AC-6 string byte limit

        [Test]
        public void test_username_255_bytes_encodes_and_round_trips()
        {
            string username = new string('a', MAX_STRING_BYTES);
            byte[] body = AuthBodyCodec.Encode(new ConnectionRequest(username, string.Empty, MakeToken()));

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Username, Is.EqualTo(username));
        }

        [Test]
        public void test_username_256_bytes_throws()
        {
            var request = new ConnectionRequest(new string('a', MAX_STRING_BYTES + 1), string.Empty, MakeToken());

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_password_64_four_byte_characters_throws()
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < EMOJI_COUNT_OVER_LIMIT; i++)
            {
                builder.Append(EMOJI);
            }

            var request = new ConnectionRequest(USERNAME, builder.ToString(), MakeToken());

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_null_string_throws_argument_null()
        {
            var request = new ConnectionRequest(null, string.Empty, MakeToken());

            Assert.Throws<ArgumentNullException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_prefix_patched_to_256_with_bytes_following_fails()
        {
            byte[] body = AuthBodyCodec.Encode(
                new ConnectionRequest(new string('a', MAX_STRING_BYTES), string.Empty, MakeToken()));
            body[0] = 0x00;
            body[1] = 0x01; // 256 little-endian; 273 bytes follow the prefix

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest _), Is.False);
        }

        [Test]
        public void test_registration_request_password_and_character_name_255_bytes_round_trip()
        {
            string limit = new string('b', MAX_STRING_BYTES);
            byte[] body = AuthBodyCodec.Encode(new RegistrationRequest(USERNAME, limit, limit, CLASS_TYPE));

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationRequest decoded), Is.True);
            Assert.That(decoded.Password, Is.EqualTo(limit));
            Assert.That(decoded.CharacterName, Is.EqualTo(limit));
        }

        [Test]
        public void test_registration_request_password_256_bytes_throws()
        {
            var request = new RegistrationRequest(USERNAME, new string('b', MAX_STRING_BYTES + 1), CHARACTER_NAME, CLASS_TYPE);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_registration_request_character_name_256_bytes_throws()
        {
            var request = new RegistrationRequest(USERNAME, PASSWORD, new string('c', MAX_STRING_BYTES + 1), CLASS_TYPE);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(request));
        }

        [Test]
        public void test_registration_request_character_name_prefix_patched_to_256_fails()
        {
            const string NAME = "a";
            const string PASS = "b";
            // Layout: [01 00 'a'] [01 00 'b'] [prefix lo, prefix hi, ...name...] [classType]
            const int CHARACTER_NAME_PREFIX_OFFSET = 6;
            // The trailing byte makes the patched body otherwise well-formed (256 string bytes + classType),
            // so only the 255-byte limit can reject it.
            byte[] body = WithTrailingByte(AuthBodyCodec.Encode(
                new RegistrationRequest(NAME, PASS, new string('c', MAX_STRING_BYTES), CLASS_TYPE)));
            body[CHARACTER_NAME_PREFIX_OFFSET] = 0x00;
            body[CHARACTER_NAME_PREFIX_OFFSET + 1] = 0x01; // 256 little-endian

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationRequest _), Is.False);
        }

        [Test]
        public void test_username_255_bytes_of_multibyte_characters_encodes()
        {
            // 63 four-byte characters (252 bytes) + 3 ASCII = exactly 255 bytes, 66 characters.
            const int EMOJI_COUNT_AT_LIMIT = 63;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < EMOJI_COUNT_AT_LIMIT; i++)
            {
                builder.Append(EMOJI);
            }

            builder.Append("abc");
            string username = builder.ToString();
            byte[] body = AuthBodyCodec.Encode(new ConnectionRequest(username, string.Empty, MakeToken()));

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Username, Is.EqualTo(username));
        }

        // --------------------------------------------------------------- Review follow-ups (code review 2026-11-06)

        [Test]
        public void test_auth_failure_reason_values_match_wire_spec()
        {
            Assert.That((byte)AuthFailureReason.None, Is.EqualTo(0));
            Assert.That((byte)AuthFailureReason.InvalidCredentials, Is.EqualTo(1));
            Assert.That((byte)AuthFailureReason.ServerStarting, Is.EqualTo(2));
            Assert.That((byte)AuthFailureReason.ServerError, Is.EqualTo(255));
            Assert.That(Enum.GetValues(typeof(AuthFailureReason)).Length, Is.EqualTo(4));
        }

        [Test]
        public void test_registration_error_values_match_wire_spec()
        {
            Assert.That((byte)RegistrationError.UsernameTaken, Is.EqualTo(0));
            Assert.That((byte)RegistrationError.UsernameTooShort, Is.EqualTo(1));
            Assert.That((byte)RegistrationError.UsernameTooLong, Is.EqualTo(2));
            Assert.That((byte)RegistrationError.UsernameInvalidChars, Is.EqualTo(3));
            Assert.That((byte)RegistrationError.PasswordTooShort, Is.EqualTo(4));
            Assert.That((byte)RegistrationError.PasswordTooLong, Is.EqualTo(5));
            Assert.That((byte)RegistrationError.ClassInvalid, Is.EqualTo(6));
            Assert.That((byte)RegistrationError.CharacterNameTaken, Is.EqualTo(7));
            Assert.That((byte)RegistrationError.CharacterNameTooShort, Is.EqualTo(8));
            Assert.That((byte)RegistrationError.CharacterNameTooLong, Is.EqualTo(9));
            Assert.That((byte)RegistrationError.CharacterNameInvalidChars, Is.EqualTo(10));
            Assert.That((byte)RegistrationError.CharacterNameMatchesUsername, Is.EqualTo(11));
            Assert.That((byte)RegistrationError.ServerError, Is.EqualTo(255));
            Assert.That(Enum.GetValues(typeof(RegistrationError)).Length, Is.EqualTo(13));
        }

        [Test]
        public void test_login_response_failure_unknown_reason_decodes_as_is()
        {
            const byte UNKNOWN_REASON = 0x63;
            byte[] body = { 0x00, UNKNOWN_REASON };

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse decoded), Is.True);
            Assert.That((byte)decoded.ErrorCode, Is.EqualTo(UNKNOWN_REASON));
        }

        [Test]
        public void test_registration_result_unknown_error_decodes_as_is()
        {
            const byte UNKNOWN_ERROR = 0x63;
            byte[] body = { 0x00, UNKNOWN_ERROR };

            Assert.That(AuthBodyCodec.TryDecode(body, out RegistrationResult decoded), Is.True);
            Assert.That((byte)decoded.ErrorCode, Is.EqualTo(UNKNOWN_ERROR));
        }

        [Test]
        public void test_login_response_success_with_failure_reason_decode_fails()
        {
            byte[] body = AuthBodyCodec.Encode(MakeLoginSuccess());
            body[1] = (byte)AuthFailureReason.InvalidCredentials;

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse _), Is.False);
        }

        [Test]
        public void test_login_response_failure_with_none_reason_decode_fails()
        {
            byte[] body = { 0x00, (byte)AuthFailureReason.None };

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse _), Is.False);
        }

        [Test]
        public void test_login_response_success_with_failure_reason_encode_throws()
        {
            var response = new LoginResponse(true, AuthFailureReason.ServerError, MakeToken(),
                new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT);

            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(response));
        }

        [Test]
        public void test_login_response_failure_with_none_reason_encode_throws()
        {
            Assert.Throws<ArgumentException>(() => AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.None)));
        }

        [Test]
        public void test_login_response_failure_ignores_success_only_fields_on_encode()
        {
            var response = new LoginResponse(false, AuthFailureReason.InvalidCredentials, MakeToken(),
                new CharacterID(CHARACTER_ID), new ZoneID(ZONE_ID), ZONE_PORT);
            byte[] expected = { 0x00, 0x01 };

            Assert.That(AuthBodyCodec.Encode(response), Is.EqualTo(expected));
        }

        [Test]
        public void test_login_response_failure_decodes_with_null_token_and_invalid_ids()
        {
            byte[] body = AuthBodyCodec.Encode(LoginResponse.ForFailure(AuthFailureReason.ServerError));

            Assert.That(AuthBodyCodec.TryDecode(body, out LoginResponse decoded), Is.True);
            Assert.That(decoded.SessionToken, Is.Null);
            Assert.That(decoded.CharacterId, Is.EqualTo(CharacterID.Invalid));
            Assert.That(decoded.ZoneId, Is.EqualTo(ZoneID.Invalid));
        }

        [Test]
        public void test_decoded_login_response_token_is_fresh_copy()
        {
            const int TOKEN_OFFSET = 2;
            byte[] body = AuthBodyCodec.Encode(MakeLoginSuccess());
            AuthBodyCodec.TryDecode(body, out LoginResponse decoded);

            body[TOKEN_OFFSET] = 0xFF;

            Assert.That(decoded.SessionToken, Is.EqualTo(MakeToken()));
        }

        [Test]
        public void test_connection_request_invalid_utf8_decodes_without_throwing()
        {
            const byte INVALID_UTF8_BYTE = 0xFF;
            const int FIRST_USERNAME_BYTE_OFFSET = 2;
            byte[] body = AuthBodyCodec.Encode(MakeConnectionRequest());
            body[FIRST_USERNAME_BYTE_OFFSET] = INVALID_UTF8_BYTE;

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Username, Does.Contain("�"));
        }
    }
}
