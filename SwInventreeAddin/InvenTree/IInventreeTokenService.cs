using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Fetches an InvenTree API token by exchanging username + password
    /// against the /api/user/token/ endpoint.
    /// </summary>
    public interface IInventreeTokenService
    {
        /// <summary>
        /// Returns the API token for the given credentials.
        /// Throws <see cref="System.InvalidOperationException"/> with a human-readable
        /// message on bad credentials (401), network failure, or malformed response.
        /// </summary>
        Task<string> GetTokenAsync(string url, string username, string password);
    }
}
