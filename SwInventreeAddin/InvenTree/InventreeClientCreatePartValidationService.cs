using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Production adapter for <see cref="ICreatePartValidationService"/> that checks
    /// IPN availability through <see cref="IInventreeClient"/> and extracts IPN
    /// validation errors from InvenTree API exception messages.
    /// </summary>
    public class InventreeClientCreatePartValidationService : ICreatePartValidationService
    {
        private readonly IInventreeClient _client;

        public InventreeClientCreatePartValidationService(IInventreeClient client)
        {
            _client = client;
        }

        /// <inheritdoc/>
        public async Task<IpnValidationResult> CheckIpnAvailableAsync(string ipn)
        {
            if (string.IsNullOrWhiteSpace(ipn))
                return IpnValidationResult.Available();

            var existing = await _client.GetPartByIpnAsync(ipn.Trim()).ConfigureAwait(false);
            if (existing != null)
                return IpnValidationResult.Unavailable(
                    $"IPN '{ipn}' already exists. Enter a different IPN.");

            return IpnValidationResult.Available();
        }

        /// <inheritdoc/>
        public string? ExtractIpnError(string exceptionMessage)
        {
            var jsonStart = exceptionMessage.IndexOf('{');
            if (jsonStart < 0) return null;

            try
            {
                var json = exceptionMessage.Substring(jsonStart);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("ipn", out var ipnErrors) &&
                    ipnErrors.ValueKind == JsonValueKind.Array)
                {
                    var errors = new List<string>();
                    foreach (var element in ipnErrors.EnumerateArray())
                    {
                        if (element.ValueKind == JsonValueKind.String)
                            errors.Add(element.GetString() ?? string.Empty);
                    }
                    return errors.Count > 0 ? string.Join(" ", errors) : null;
                }
            }
            catch
            {
                // Ignore malformed JSON; the caller will show the raw message.
            }

            return null;
        }
    }
}
