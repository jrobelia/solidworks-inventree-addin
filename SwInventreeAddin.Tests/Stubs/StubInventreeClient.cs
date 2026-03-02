using System;
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
    }
}