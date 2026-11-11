using System;
using IronGrind.Authentication;
using IronGrind.Client.Auth;

namespace IronGrind.Client.Session
{
    /// <summary>Login seam used by <see cref="ClientZoneSession"/>; the production implementation wraps <see cref="AuthClient"/>.</summary>
    public interface IZoneLoginService
    {
        /// <summary>
        /// Logs in. A null token is the credential path; a 16-byte token is the token path (the password is then
        /// ignored by the gateway client and sent as an empty string). The implementation must not keep the
        /// <paramref name="token"/> array.
        /// </summary>
        /// <param name="username">Account username.</param>
        /// <param name="password">Password (the empty string on the token path).</param>
        /// <param name="token">16-byte session token, or null.</param>
        /// <param name="onDone">Called exactly once with the result.</param>
        void Login(string username, string password, byte[] token, Action<LoginResponse> onDone);
    }

    /// <summary><see cref="IZoneLoginService"/> over <see cref="AuthClient"/>. ServerStarting retries stay inside <see cref="AuthClient"/>.</summary>
    public sealed class AuthClientLoginService : IZoneLoginService
    {
        private readonly AuthClient _client;

        /// <summary>Creates the adapter.</summary>
        /// <param name="client">The auth client to call.</param>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is null.</exception>
        public AuthClientLoginService(AuthClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <inheritdoc/>
        public void Login(string username, string password, byte[] token, Action<LoginResponse> onDone)
        {
            _client.Login(username, password, token, onDone, null);
        }
    }
}
