namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Domain seam for extracting IPN field errors from InvenTree validation
    /// responses embedded in exception messages.
    /// </summary>
    public interface ICreatePartValidationService
    {
        /// <summary>
        /// Attempts to extract the first IPN field error from an InvenTree validation
        /// response embedded in an exception message.
        /// </summary>
        string? ExtractIpnError(string exceptionMessage);
    }
}
