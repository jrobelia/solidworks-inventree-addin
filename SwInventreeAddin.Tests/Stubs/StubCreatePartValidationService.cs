using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// In-memory stub for <see cref="ICreatePartValidationService"/>.
    /// </summary>
    public class StubCreatePartValidationService : ICreatePartValidationService
    {
        /// <summary>Value to return from <see cref="ExtractIpnError"/>.</summary>
        public string? ExtractedError { get; set; }

        public string? ExtractIpnError(string exceptionMessage)
            => ExtractedError;
    }
}
