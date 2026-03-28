using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests.Stubs
{
    public class StubInventreeClient : IInventreeClient
    {
        public InventreePart? PartToReturn        { get; set; }
        public string         LastIpnRequested    { get; private set; } = string.Empty;
        public int            LastPushedPk        { get; private set; }
        public string         LastPushedRevision  { get; private set; } = string.Empty;
        public string         LastPushedName      { get; private set; } = string.Empty;
        public string         LastPushedNotes     { get; private set; } = string.Empty;
        public Exception?     ThrowOnUpdate       { get; set; }
        public int            LastUploadedPk        { get; private set; }
        public byte[]?        LastUploadedImageData { get; private set; }
        public Exception?     ThrowOnUpload         { get; set; }

        public Task<InventreePart?> GetPartByIpnAsync(string ipn)
        {
            LastIpnRequested = ipn;
            return Task.FromResult(PartToReturn);
        }

        public Task UpdatePartRevisionAsync(int pk, string revision)
        {
            if (ThrowOnUpdate != null) throw ThrowOnUpdate;
            LastPushedPk       = pk;
            LastPushedRevision = revision;
            return Task.CompletedTask;
        }

        public Task UpdatePartNameAsync(int pk, string name)
        {
            if (ThrowOnUpdate != null) throw ThrowOnUpdate;
            LastPushedPk   = pk;
            LastPushedName = name;
            return Task.CompletedTask;
        }

        public Task UpdatePartNotesAsync(int pk, string notes)
        {
            if (ThrowOnUpdate != null) throw ThrowOnUpdate;
            LastPushedPk    = pk;
            LastPushedNotes = notes;
            return Task.CompletedTask;
        }

        public Task UploadPartImageAsync(int pk, byte[] pngData)
        {
            if (ThrowOnUpload != null) throw ThrowOnUpload;
            LastUploadedPk        = pk;
            LastUploadedImageData = pngData;
            return Task.CompletedTask;
        }

        public byte[]?   ThumbnailBytesToReturn { get; set; }
        public int        DownloadImageCallCount { get; private set; }
        public Exception? ThrowOnDownload        { get; set; }

        public Task<byte[]?> DownloadImageAsync(string url)
        {
            DownloadImageCallCount++;
            if (ThrowOnDownload != null) throw ThrowOnDownload;
            return Task.FromResult(ThumbnailBytesToReturn);
        }

        public InventreeServerInfo? ServerInfoToReturn { get; set; }
        public Exception? ThrowOnGetServerInfo { get; set; }

        public Task<InventreeServerInfo> GetServerInfoAsync()
        {
            if (ThrowOnGetServerInfo != null) throw ThrowOnGetServerInfo;
            return Task.FromResult(ServerInfoToReturn ?? new InventreeServerInfo());
        }

        // ── GetCategoriesAsync ─────────────────────────────────────────────────

        public IReadOnlyList<InventreeCategory> CategoriesToReturn { get; set; }
            = new List<InventreeCategory>();
        public int?  LastGetCategoriesParentId { get; private set; }
        public bool  ThrowOnGetCategories { get; set; }

        public Task<IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId)
        {
            LastGetCategoriesParentId = parentId;
            if (ThrowOnGetCategories)
                throw new HttpRequestException("Stub: GetCategories failed");
            return Task.FromResult(CategoriesToReturn);
        }

        // ── CreatePartAsync ────────────────────────────────────────────────────

        public int    PkToReturnOnCreate  { get; set; }
        public int    LastCreateCategoryPk { get; private set; }
        public string LastCreateName       { get; private set; } = string.Empty;
        public bool   ThrowOnCreate        { get; set; }

        public Task<int> CreatePartAsync(int categoryPk, string name)
        {
            LastCreateCategoryPk = categoryPk;
            LastCreateName       = name;
            if (ThrowOnCreate)
                throw new HttpRequestException("Stub: CreatePart failed");
            return Task.FromResult(PkToReturnOnCreate);
        }

        // ── GetPartByPkAsync ───────────────────────────────────────────────────

        public InventreePart? PartByPkToReturn  { get; set; }
        public int            LastGetPartByPkPk { get; private set; }
        public bool           ThrowOnGetPartByPk { get; set; }

        // Queue successive return values for polling tests.
        // When the queue runs out, falls back to PartByPkToReturn.
        private Queue<InventreePart?> _partByPkQueue;

        public void QueuePartByPkResponses(params InventreePart?[] parts)
        {
            _partByPkQueue = new Queue<InventreePart?>(parts);
        }

        public Task<InventreePart?> GetPartByPkAsync(int pk)
        {
            LastGetPartByPkPk = pk;
            if (ThrowOnGetPartByPk)
                throw new HttpRequestException("Stub: GetPartByPk failed");
            if (_partByPkQueue != null && _partByPkQueue.Count > 0)
                return Task.FromResult(_partByPkQueue.Dequeue());
            return Task.FromResult(PartByPkToReturn);
        }
    }
}