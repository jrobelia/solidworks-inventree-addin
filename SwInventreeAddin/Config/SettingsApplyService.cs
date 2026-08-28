using System;
using System.Threading.Tasks;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Validates, resolves credentials for, and persists the server settings.
    /// </summary>
    public class SettingsApplyService : ISettingsApplyService
    {
        private readonly IConfigProvider _configProvider;
        private readonly IInventreeTokenService _tokenService;

        /// <summary>Uses the supplied config and token services.</summary>
        public SettingsApplyService(IConfigProvider configProvider, IInventreeTokenService tokenService)
        {
            _configProvider = configProvider;
            _tokenService   = tokenService;
        }

        /// <inheritdoc/>
        public async Task<string> ResolveApiKeyAsync(SettingsApplyInput input)
        {
            var url = input.Url.Trim();

            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("Server URL is required.");

            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Server URL must begin with https:// — a plain http:// connection is not secure.");

            var username = input.Username.Trim();
            var password = input.Password;
            var rawKey   = input.RawApiKey.Trim();

            if (!string.IsNullOrWhiteSpace(username) || !string.IsNullOrWhiteSpace(password))
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    throw new InvalidOperationException("Enter both username and password.");

                return await _tokenService.GetTokenAsync(url, username, password)
                                          .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(rawKey))
                return rawKey;

            throw new InvalidOperationException(
                "Enter a username and password, or expand Advanced and paste an API key.");
        }

        /// <inheritdoc/>
        public async Task ApplyAsync(SettingsApplyInput input)
        {
            string apiKey;
            try
            {
                apiKey = await ResolveApiKeyAsync(input).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new SettingsApplyException(
                    $"Failed to save server settings: {ex.Message}",
                    SettingsApplyErrorKind.Config,
                    ex);
            }

            try
            {
                _configProvider.SaveServerConfig(new ServerConfig
                {
                    Url               = input.Url.Trim(),
                    ApiKey            = apiKey,
                    MappingSourcePath = input.SharedMappingPath,
                    BomKeyword        = string.IsNullOrWhiteSpace(input.BomKeyword)
                                            ? "inventree"
                                            : input.BomKeyword.Trim(),
                    WaitForAutoPartNumber = input.WaitForAutoPartNumber,
                });
            }
            catch (Exception ex)
            {
                throw new SettingsApplyException(
                    $"Failed to save server settings: {ex.Message}",
                    SettingsApplyErrorKind.Config,
                    ex);
            }
        }
    }
}
