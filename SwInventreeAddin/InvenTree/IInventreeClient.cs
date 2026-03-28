using System.Collections.Generic;
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

        /// <summary>
        /// Returns the immediate children of the given category, or all top-level
        /// categories when <paramref name="parentId"/> is null.
        /// </summary>
        Task<IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId);

        /// <summary>Creates a new part and returns its server-assigned PK.</summary>
        Task<int> CreatePartAsync(int categoryPk, string name, string? ipn = null);

        /// <summary>Fetches a single part by its primary key. Returns null when not found.</summary>
        Task<InventreePart?> GetPartByPkAsync(int pk);
    }
}
