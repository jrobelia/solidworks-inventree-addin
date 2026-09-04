using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    /// <summary>
    /// In-memory stub for <see cref="ICreatePartValidationErrorService"/>.
    /// </summary>
    public class StubCreatePartValidationErrorService : ICreatePartValidationErrorService
    {
        /// <summary>Value to return from <see cref="ExtractIpnError"/>.</summary>
        public string? ExtractedError { get; set; }

        public string? ExtractIpnError(string exceptionMessage)
            => ExtractedError;
    }
}
