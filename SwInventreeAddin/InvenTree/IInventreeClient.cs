using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    public interface IInventreeClient
    {
        Task<InventreePart?> GetPartByIpnAsync(string ipn);
        Task UpdatePartRevisionAsync(int pk, string revision);
        Task UpdatePartNameAsync(int pk, string name);
        Task UpdatePartNotesAsync(int pk, string notes);
        Task UploadPartImageAsync(int pk, byte[] pngData);
        Task<byte[]?> DownloadImageAsync(string url);
        Task<InventreeServerInfo> GetServerInfoAsync();
    }
}
