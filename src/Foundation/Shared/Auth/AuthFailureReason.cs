namespace IronGrind.Authentication
{
    /// <summary>
    /// Failure code carried in a <see cref="LoginResponse"/> (auth-wire-messages.md).
    /// Decoding does not range-check the byte: unknown values are passed through as-is.
    /// </summary>
    public enum AuthFailureReason : byte
    {
        /// <summary>No failure (used when the login succeeded).</summary>
        None = 0,

        /// <summary>Username/password or session token was rejected.</summary>
        InvalidCredentials = 1,

        /// <summary>The server is still starting and cannot accept logins yet.</summary>
        ServerStarting = 2,

        /// <summary>Unspecified server-side error.</summary>
        ServerError = 255
    }
}
