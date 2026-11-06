namespace IronGrind.Authentication
{
    /// <summary>
    /// Failure code carried in a <see cref="RegistrationResult"/> (auth-wire-messages.md).
    /// Decoding does not range-check the byte: unknown values are passed through as-is.
    /// </summary>
    public enum RegistrationError : byte
    {
        /// <summary>The username is already registered.</summary>
        UsernameTaken = 0,

        /// <summary>The username is shorter than the minimum length.</summary>
        UsernameTooShort = 1,

        /// <summary>The username is longer than the maximum length.</summary>
        UsernameTooLong = 2,

        /// <summary>The username contains disallowed characters.</summary>
        UsernameInvalidChars = 3,

        /// <summary>The password is shorter than the minimum length.</summary>
        PasswordTooShort = 4,

        /// <summary>The password is longer than the maximum length.</summary>
        PasswordTooLong = 5,

        /// <summary>The chosen class type is not valid.</summary>
        ClassInvalid = 6,

        /// <summary>The character name is already in use.</summary>
        CharacterNameTaken = 7,

        /// <summary>The character name is shorter than the minimum length.</summary>
        CharacterNameTooShort = 8,

        /// <summary>The character name is longer than the maximum length.</summary>
        CharacterNameTooLong = 9,

        /// <summary>The character name contains disallowed characters.</summary>
        CharacterNameInvalidChars = 10,

        /// <summary>The character name equals the username.</summary>
        CharacterNameMatchesUsername = 11,

        /// <summary>Unspecified server-side error.</summary>
        ServerError = 255
    }
}
