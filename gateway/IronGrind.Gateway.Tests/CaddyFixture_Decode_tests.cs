using System;
using System.IO;
using IronGrind.Authentication;
using NUnit.Framework;

namespace IronGrind.Gateway.Tests
{
    /// <summary>
    /// Authentication Story 016: the committed Caddy check fixture (<c>tools/caddy/fixtures/login-unknown-user.bin</c>)
    /// is a well-formed <see cref="ConnectionRequest"/> for an unregistered username with an all-zero token, so the TLS
    /// login check gets <c>InvalidCredentials</c> and not a malformed-body 400.
    /// </summary>
    [TestFixture]
    internal sealed class CaddyFixture_Decode_tests
    {
        private const string FIXTURE_RELATIVE_PATH = "tools/caddy/fixtures/login-unknown-user.bin";
        private const string REPO_MARKER_DIRECTORY = "tools/caddy";
        private const string USERNAME = "caddy_check_unknown";
        private const string PASSWORD = "not-a-real-password";

        [Test]
        public void test_login_unknown_user_fixture_decodes_as_connection_request()
        {
            byte[] body = File.ReadAllBytes(FindFixturePath());

            Assert.That(AuthBodyCodec.TryDecode(body, out ConnectionRequest decoded), Is.True);
            Assert.That(decoded.Username, Is.EqualTo(USERNAME));
            Assert.That(decoded.Password, Is.EqualTo(PASSWORD));
            Assert.That(decoded.TokenField, Is.EqualTo(new byte[AuthBodyCodec.TOKEN_LENGTH]));
        }

        [Test]
        public void test_login_unknown_user_fixture_truncated_by_one_byte_is_rejected()
        {
            // Control: the decode test above can fail. A fixture one byte short (missing the token's last byte) is malformed.
            byte[] body = File.ReadAllBytes(FindFixturePath());

            Assert.That(AuthBodyCodec.TryDecode(body.AsSpan(0, body.Length - 1), out ConnectionRequest _), Is.False);
        }

        /// <summary>Walks up from the test binary directory until the repository root (the folder holding <c>tools/caddy</c>).</summary>
        private static string FindFixturePath()
        {
            string? directory = TestContext.CurrentContext.TestDirectory;
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory, REPO_MARKER_DIRECTORY)))
                {
                    return Path.Combine(directory, FIXTURE_RELATIVE_PATH);
                }

                directory = Path.GetDirectoryName(directory);
            }

            throw new DirectoryNotFoundException(
                $"Repository root (containing {REPO_MARKER_DIRECTORY}) not found above {TestContext.CurrentContext.TestDirectory}.");
        }
    }
}
