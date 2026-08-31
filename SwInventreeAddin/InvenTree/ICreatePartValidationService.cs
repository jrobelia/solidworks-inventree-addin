using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Domain seam for validating an IPN before creating an InvenTree part.
    /// Hides the InvenTree client call and the exception-message JSON parsing
    /// that <see cref="CreatePartViewModel"/> previously owned.
    /// </summary>
    public interface ICreatePartValidationService
    {
        /// <summary>
        /// Checks whether <paramref name="ipn"/> is already in use on the server.
        /// Returns an <see cref="IpnValidationResult.Unavailable"/> result when it is,
        /// and <see cref="IpnValidationResult.Available"/> when it is not.
        /// </summary>
        Task<IpnValidationResult> CheckIpnAvailableAsync(string ipn);

        /// <summary>
        /// Attempts to extract the first IPN field error from an InvenTree validation
        /// response embedded in an exception message.
        /// </summary>
        string? ExtractIpnError(string exceptionMessage);
    }
}
