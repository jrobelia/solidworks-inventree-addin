using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    public class InventreeHttpClient : IInventreeClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public InventreeHttpClient(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
        }

        public Task<InventreePart> GetPartByIpnAsync(string ipn)
        {
            throw new NotImplementedException();
        }
    }
}
