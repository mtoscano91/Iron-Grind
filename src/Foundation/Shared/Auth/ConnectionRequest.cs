namespace IronGrind.Authentication
{
    /// <summary>
    /// Login request body (client to gateway). Either a username/password login or a
    /// token-resume login (password is <c>""</c>, never null).
    /// </summary>
    public readonly struct ConnectionRequest
    {
        /// <summary>Account username.</summary>
        public readonly string Username;

        /// <summary>Account password; <c>""</c> on the token path, never null.</summary>
        public readonly string Password;

        /// <summary>
        /// Session token field; exactly 16 bytes when encoded. The array is held by reference, not copied:
        /// changing its contents after construction changes this request.
        /// </summary>
        public readonly byte[] TokenField;

        /// <summary>Creates a request. Field validity is checked by <see cref="AuthBodyCodec"/> on encode.</summary>
        /// <param name="username">Account username.</param>
        /// <param name="password">Account password, or <c>""</c> on the token path.</param>
        /// <param name="tokenField">16-byte token field.</param>
        public ConnectionRequest(string username, string password, byte[] tokenField)
        {
            Username = username;
            Password = password;
            TokenField = tokenField;
        }
    }
}
