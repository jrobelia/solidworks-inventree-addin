using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    public interface IInventreeClient
    {
        Task<InventreePart?> GetPartByIpnAsync(string ipn);
        Task UpdatePartRevisionAsync(int pk, string revision);
    }
}
