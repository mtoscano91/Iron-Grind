namespace IronGrind.Authentication
{
    /// <summary>Registration request body (client to gateway): new account plus first character.</summary>
    public readonly struct RegistrationRequest
    {
        /// <summary>Requested account username.</summary>
        public readonly string Username;

        /// <summary>Requested account password.</summary>
        public readonly string Password;

        /// <summary>Requested character name.</summary>
        public readonly string CharacterName;

        /// <summary>Requested character class (raw byte, validated server-side).</summary>
        public readonly byte ClassType;

        /// <summary>Creates a request. String limits are checked by <see cref="AuthBodyCodec"/> on encode.</summary>
        /// <param name="username">Requested username.</param>
        /// <param name="password">Requested password.</param>
        /// <param name="characterName">Requested character name.</param>
        /// <param name="classType">Requested class type.</param>
        public RegistrationRequest(string username, string password, string characterName, byte classType)
        {
            Username = username;
            Password = password;
            CharacterName = characterName;
            ClassType = classType;
        }
    }
}
