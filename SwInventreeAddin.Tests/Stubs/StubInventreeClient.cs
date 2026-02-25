using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubInventreeClient : IInventreeClient
    {
        public InventreePart? PartToReturn { get; set; }
        public string LastIpnRequested { get; private set; } = string.Empty;

        public Task<InventreePart?> GetPartByIpnAsync(string ipn)
        {
            LastIpnRequested = ipn;
            return Task.FromResult(PartToReturn);
        }
    }
}
