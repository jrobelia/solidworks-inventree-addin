using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;

namespace SwInventreeAddin.InvenTree
{
    public interface IInventreeClient
    {
        Task<InventreePart?> GetPartByIpnAsync(string ipn);
        Task UpdatePartRevisionAsync(int pk, string revision);
        Task UpdatePartNameAsync(int pk, string name);
        Task UpdatePartNotesAsync(int pk, string notes);
        Task UpdatePartDescriptionAsync(int pk, string description);
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

        /// <summary>Returns all BOM lines for the given assembly part PK.
        /// Populates Validated and HasSubstitutes from the response.</summary>
        Task<IReadOnlyList<InventreeBomLine>> GetBomAsync(int assemblyPk);

        /// <summary>Creates a new BOM line. Returns the server-assigned line PK.</summary>
        Task<int> CreateBomLineAsync(int assemblyPk, int subPartPk, decimal quantity,
            string reference, string note, bool consumable, bool optional);

        /// <summary>Updates Qty/Reference/Note/Consumable/Optional on an existing BOM line (PATCH).
        /// Must NOT include the substitutes field in the request body.</summary>
        Task UpdateBomLineAsync(int bomLinePk, decimal quantity,
            string reference, string note, bool consumable, bool optional);

        /// <summary>Returns ALL parts matching the given IPN. May return 0, 1, or many.
        /// Never truncates. Callers handle the multi-result case explicitly.</summary>
        Task<IReadOnlyList<InventreePart>> GetPartsByIpnAsync(string ipn);

        /// <summary>Builds the public InvenTree part detail URL for the given PK.
        /// Returns null when no server base address is configured.</summary>
        Uri? GetPartWebUrl(int pk);
    }
}
