using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SwInventreeAddin.InvenTree
{
    /// <summary>
    /// Production adapter for <see cref="ICreatePartValidationErrorService"/> that
    /// extracts IPN validation errors from InvenTree API exception messages.
    /// </summary>
    public class InventreeClientCreatePartValidationErrorService : ICreatePartValidationErrorService
    {
        /// <inheritdoc/>
        public string? ExtractIpnError(string exceptionMessage)
        {
            var jsonStart = exceptionMessage.IndexOf('{');
            if (jsonStart < 0) return null;

            try
            {
                var json = exceptionMessage.Substring(jsonStart);
                using var doc = JsonDocument.Parse(json);
                // InvenTree keys field errors by the serializer field name "IPN";
                // fall back to lowercase "ipn" defensively.
                var found = doc.RootElement.TryGetProperty("IPN", out var ipnErrors) ||
                            doc.RootElement.TryGetProperty("ipn", out ipnErrors);
                if (found && ipnErrors.ValueKind == JsonValueKind.Array)
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
