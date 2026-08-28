using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubInventreeTokenService : IInventreeTokenService
    {
        public string? TokenToReturn { get; set; }
        public string? LastUrl { get; private set; }
        public string? LastUsername { get; private set; }
        public string? LastPassword { get; private set; }

        public Task<string> GetTokenAsync(string url, string username, string password)
        {
            LastUrl      = url;
            LastUsername = username;
            LastPassword = password;

            if (TokenToReturn == null)
                throw new System.InvalidOperationException("Stub token service configured to fail.");

            return Task.FromResult(TokenToReturn);
        }
    }
}
