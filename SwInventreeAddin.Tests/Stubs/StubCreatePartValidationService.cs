using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// In-memory stub for <see cref="ICreatePartValidationService"/>.
    /// </summary>
    public class StubCreatePartValidationService : ICreatePartValidationService
    {
        /// <summary>The part to pretend is already using the supplied IPN.</summary>
        public InventreePart? ExistingPart { get; set; }

        /// <summary>Value to return from <see cref="ExtractIpnError"/>.</summary>
        public string? ExtractedError { get; set; }

        /// <summary>Exception to throw from <see cref="CheckIpnAvailableAsync"/>.</summary>
        public System.Exception? ThrowOnCheck { get; set; }

        public string LastIpnChecked { get; private set; } = string.Empty;

        public Task<IpnValidationResult> CheckIpnAvailableAsync(string ipn)
        {
            if (ThrowOnCheck != null)
                throw ThrowOnCheck;

            LastIpnChecked = ipn;

            if (ExistingPart != null)
                return Task.FromResult(
                    IpnValidationResult.Unavailable(
                        $"IPN '{ipn}' already exists. Enter a different IPN."));

            return Task.FromResult(IpnValidationResult.Available());
        }

        public string? ExtractIpnError(string exceptionMessage)
            => ExtractedError;
    }
}
