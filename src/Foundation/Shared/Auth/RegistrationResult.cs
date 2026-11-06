namespace IronGrind.Authentication
{
    /// <summary>Registration response body (gateway to client). Always 2 bytes.</summary>
    public readonly struct RegistrationResult
    {
        /// <summary>True if the registration succeeded.</summary>
        public readonly bool Success;

        /// <summary>Failure reason; meaningful only when <see cref="Success"/> is false.</summary>
        public readonly RegistrationError ErrorCode;

        /// <summary>Creates a result.</summary>
        /// <param name="success">Whether registration succeeded.</param>
        /// <param name="errorCode">Failure reason.</param>
        public RegistrationResult(bool success, RegistrationError errorCode)
        {
            Success = success;
            ErrorCode = errorCode;
        }
    }
}
