using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using SwInventreeAddin.Bom;

namespace SwInventreeAddin.InvenTree
{
    public class InventreeHttpClient : IInventreeClient
    {
        private readonly HttpClient _httpClient;

        public InventreeHttpClient(HttpClient httpClient, string apiKey)
        {
            _httpClient = httpClient;
            // Set the auth header once — the API key never changes during the session.
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Token", apiKey);
        }

        public async Task<InventreePart?> GetPartByIpnAsync(string ipn)
        {
            var parts = await GetPartsByIpnAsync(ipn).ConfigureAwait(false);
            if (parts.Count == 0) return null;
            if (parts.Count  > 1)
                throw new InvalidOperationException(
                    $"Duplicate IPN '{ipn}': {parts.Count} parts found. Resolve duplicates in InvenTree.");
            return parts[0];
        }

        public async Task<IReadOnlyList<InventreePart>> GetPartsByIpnAsync(string ipn)
        {
            // IPN comes from SolidWorks custom properties (user-controlled) — must be encoded
            // to prevent query-string injection (e.g. "ABC&limit=0").
            using var listRequest = new HttpRequestMessage(
                HttpMethod.Get, $"/api/part/?IPN={Uri.EscapeDataString(ipn)}&limit=0");

            var listResponse = await _httpClient.SendAsync(listRequest).ConfigureAwait(false);

            if (!listResponse.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)listResponse.StatusCode} {listResponse.StatusCode}");

            var listJson = await listResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            using var listDocument = JsonDocument.Parse(listJson);
            var root = listDocument.RootElement;

            var array = root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("results", out var resultsElement)
                ? resultsElement
                : root;

            var parts = new List<InventreePart>();
            foreach (var el in array.EnumerateArray())
            {
                int pk = el.TryGetProperty("pk", out var pkProp) ? pkProp.GetInt32() : 0;
                if (pk > 0)
                {
                    var detail = await FetchDetailAsync(pk).ConfigureAwait(false);
                    if (detail != null) { parts.Add(detail); continue; }
                }
                parts.Add(new InventreePart
                {
                    Pk       = pk,
                    Ipn      = GetString(el, "IPN"),
                    Name     = GetString(el, "name"),
                    Revision = GetString(el, "revision"),
                    Notes    = GetString(el, "notes"),
                });
            }
            return parts;
        }

        /// <summary>
        /// Fetches a single part record from /api/part/{pk}/.
        /// Returns null when the server returns a non-success status.
        /// </summary>
        private async Task<InventreePart?> FetchDetailAsync(int pk)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/part/{pk}/");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var detail = doc.RootElement;

            return new InventreePart
            {
                Pk           = pk,
                Name         = GetString(detail, "name"),
                Description  = GetString(detail, "description"),
                Notes        = GetString(detail, "notes"),
                Revision     = GetString(detail, "revision"),
                Ipn          = GetString(detail, "IPN"),
                ThumbnailUrl = GetString(detail, "thumbnail") is var t && t.Length > 0 ? t : null,
                InStock      = GetDecimal(detail, "in_stock"),
                Ordering     = GetDecimal(detail, "ordering"),
                Active       = GetBool(detail, "active"),
                IsAssembly   = GetBool(detail, "assembly"),
            };
        }

        public async Task<IReadOnlyList<InventreeCategory>> GetCategoriesAsync(int? parentId)
        {
            // When fetching children of a known parent, filter server-side.
            // When fetching root categories, fetch all and filter client-side for
            // items with no parent — avoids relying on ?parent=null which some
            // InvenTree versions reject with 400.
            var query = parentId.HasValue
                ? $"/api/part/category/?parent={parentId.Value}&limit=0"
                : "/api/part/category/?limit=0";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var array = root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("results", out var results)
                ? results
                : root;

            var list = new List<InventreeCategory>(array.GetArrayLength());
            foreach (var item in array.EnumerateArray())
            {
                var parentPk = item.TryGetProperty("parent", out var parP) && parP.ValueKind == JsonValueKind.Number
                                ? parP.GetInt32() : (int?)null;

                // When loading root categories (no parentId given), skip any item
                // that actually has a parent — they belong lower in the tree.
                if (!parentId.HasValue && parentPk.HasValue)
                    continue;

                list.Add(new InventreeCategory
                {
                    Pk          = item.TryGetProperty("pk",            out var pkP)   ? pkP.GetInt32()              : 0,
                    Name        = item.TryGetProperty("name",          out var nameP) ? nameP.GetString() ?? string.Empty : string.Empty,
                    ParentPk    = parentPk,
                    HasChildren = item.TryGetProperty("subcategories", out var subP)  && subP.ValueKind == JsonValueKind.Number
                                    ? subP.GetInt32() > 0 : false,
                });
            }
            return list;
        }

        public async Task<int> CreatePartAsync(int categoryPk, string name, string? ipn = null)
        {
            var payloadDict = new System.Collections.Generic.Dictionary<string, object>
            {
                ["category"] = categoryPk,
                ["name"]     = name
            };
            if (!string.IsNullOrWhiteSpace(ipn))
                payloadDict["ipn"] = ipn.Trim();

            var payload = JsonSerializer.Serialize(payloadDict);
            var body    = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/part/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("pk", out var pkProp))
                return pkProp.GetInt32();

            throw new InvalidOperationException("InvenTree did not return a pk for the new part.");
        }

        public Task<InventreePart?> GetPartByPkAsync(int pk) =>
            FetchDetailAsync(pk);

        public async Task<IReadOnlyList<InventreeBomLine>> GetBomAsync(int assemblyPk)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"/api/bom/?part={assemblyPk}&limit=0");
            var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var array = root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("results", out var r) ? r : root;

            var lines = new List<InventreeBomLine>();
            foreach (var el in array.EnumerateArray())
            {
                bool hasSubs = el.TryGetProperty("substitutes", out var subs) &&
                               subs.ValueKind == JsonValueKind.Array &&
                               subs.GetArrayLength() > 0;

                var line = new InventreeBomLine
                {
                    Pk             = GetInt(el, "pk"),
                    SubPartPk      = GetInt(el, "sub_part"),
                    Quantity       = GetDecimal(el, "quantity"),
                    Reference      = GetString(el, "reference"),
                    Note           = GetString(el, "note"),
                    Consumable     = GetBool(el, "consumable"),
                    Optional       = GetBool(el, "optional"),
                    Validated      = GetBool(el, "validated"),
                    HasSubstitutes = hasSubs,
                };

                if (line.SubPartPk > 0)
                {
                    var part = await FetchDetailAsync(line.SubPartPk).ConfigureAwait(false);
                    if (part != null) line.SubPartIpn = part.Ipn;
                }
                lines.Add(line);
            }
            return lines;
        }

        public async Task<int> CreateBomLineAsync(int assemblyPk, int subPartPk, decimal quantity,
            string reference, string note, bool consumable, bool optional)
        {
            var body = JsonSerializer.Serialize(new
            {
                part = assemblyPk, sub_part = subPartPk, quantity,
                reference, note, consumable, optional,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/bom/")
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode}: {detail}");
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var respDoc = JsonDocument.Parse(json);
            return GetInt(respDoc.RootElement, "pk");
        }

        public async Task UpdateBomLineAsync(int bomLinePk, decimal quantity,
            string reference, string note, bool consumable, bool optional)
        {
            var body = JsonSerializer.Serialize(new
            {
                quantity, reference, note, consumable, optional,
                // substitutes intentionally omitted — PATCH is partial; omitting preserves server value
            });
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/bom/{bomLinePk}/")
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            };
            var response = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");
        }

        public Task UpdatePartRevisionAsync(int pk, string revision)    => PatchPartAsync(pk, new { revision });
        public Task UpdatePartNameAsync(int pk, string name)            => PatchPartAsync(pk, new { name });
        public Task UpdatePartNotesAsync(int pk, string notes)          => PatchPartAsync(pk, new { notes });
        public Task UpdatePartDescriptionAsync(int pk, string description) => PatchPartAsync(pk, new { description });

        private async Task PatchPartAsync(int pk, object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var body = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = body
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        private static string GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var prop)
                ? prop.GetString() ?? string.Empty
                : string.Empty;

        private static int GetInt(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32() : 0;

        private static decimal GetDecimal(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop)) return 0m;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d)) return d;
            if (prop.ValueKind == JsonValueKind.String &&
                decimal.TryParse(prop.GetString(), System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out var ds)) return ds;
            return 0m;
        }

        private static bool GetBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop)) return false;
            if (prop.ValueKind == JsonValueKind.True)  return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            return false;
        }

        public async Task<byte[]?> DownloadImageAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            // Reject absolute non-HTTPS URLs — the auth token must not travel over plain HTTP.
            // Relative URLs (e.g. /media/thumbnails/widget.png) are safe because BaseAddress is HTTPS.
            if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
                !string.Equals(parsed.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;   // treat any error as "no image" — caller shows placeholder

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        public async Task UploadPartImageAsync(int pk, byte[] pngData)
        {
            using var content = new MultipartFormDataContent();
            using var imageContent = new ByteArrayContent(pngData);
            imageContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(imageContent, "image", "part_image.png");

            using var request = new HttpRequestMessage(
                new HttpMethod("PATCH"), $"/api/part/{pk}/")
            {
                Content = content
            };

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new HttpRequestException(
                    "InvenTree rejected the API key (401 Unauthorized).");

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree returned {(int)response.StatusCode} {response.StatusCode}");
        }

        public async Task<InventreeServerInfo> GetServerInfoAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"InvenTree API returned {(int)response.StatusCode} {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new InventreeServerInfo
            {
                ServerVersion = GetString(root, "version"),
                ApiVersion    = root.TryGetProperty("apiVersion", out var v) ? v.GetInt32() : 0,
            };
        }
    }
}
