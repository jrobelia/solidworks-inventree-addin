namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// The result of checking whether a user-supplied IPN is available for a new part.
    /// </summary>
    public class IpnValidationResult
    {
        /// <summary>
        /// True when the IPN is not already in use and can be submitted.</summary>
        public bool IsAvailable { get; }

        /// <summary>
        /// Null when <see cref="IsAvailable"/> is <c>true</c>; otherwise a user-facing
        /// error message explaining why the IPN cannot be used.</summary>
        public string? ErrorMessage { get; }

        private IpnValidationResult(bool isAvailable, string? errorMessage)
        {
            IsAvailable = isAvailable;
            ErrorMessage = errorMessage;
        }

        /// <summary>Creates a result indicating the IPN is available.</summary>
        public static IpnValidationResult Available() => new IpnValidationResult(true, null);

        /// <summary>Creates a result indicating the IPN is unavailable with the supplied error.</summary>
        public static IpnValidationResult Unavailable(string errorMessage)
            => new IpnValidationResult(false, errorMessage);
    }
}
