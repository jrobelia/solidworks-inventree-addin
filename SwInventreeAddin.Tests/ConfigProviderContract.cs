using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// The behavioural contract every <see cref="IConfigProvider"/> adapter
    /// must satisfy — production and stub alike: writes apply, reads reflect
    /// writes, deletes remove. Each method asserts through the interface only
    /// and takes a provider in the nothing-saved state; the calling fixture
    /// owns that precondition (a fresh temp file for
    /// <see cref="EncryptedConfigProvider"/>,
    /// <see cref="Stubs.StubConfigProvider.WithNoSavedConfig"/> for the stub).
    /// Same shared-infra shape as <see cref="HiddenTestWindow"/>: each adapter
    /// fixture adds one thin [Test] per behaviour, so a failure names both the
    /// adapter and the broken guarantee.
    /// </summary>
    internal static class ConfigProviderContract
    {
        public static void Get_WhenNothingSaved_ReturnsNull(IConfigProvider provider)
        {
            Assert.That(provider.GetServerConfig(), Is.Null);
        }

        public static void SaveThenGet_RoundTripsSavedValues(IConfigProvider provider)
        {
            var config = FullyPopulatedConfig();

            provider.SaveServerConfig(config);
            var result = provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result!.Url, Is.EqualTo(config.Url));
                Assert.That(result.ApiKey, Is.EqualTo(config.ApiKey));
                Assert.That(result.MappingSourcePath, Is.EqualTo(config.MappingSourcePath));
                Assert.That(result.BomKeyword, Is.EqualTo(config.BomKeyword));
                Assert.That(result.WaitForServerAssignedIpn,
                            Is.EqualTo(config.WaitForServerAssignedIpn));
            });
        }

        public static void Save_Overwrite_LastWriteWins(IConfigProvider provider)
        {
            provider.SaveServerConfig(new ServerConfig
            {
                Url = "https://first.example.com",
                ApiKey = "first-key",
            });
            var second = FullyPopulatedConfig();

            provider.SaveServerConfig(second);
            var result = provider.GetServerConfig();

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result!.Url, Is.EqualTo(second.Url));
                Assert.That(result.ApiKey, Is.EqualTo(second.ApiKey));
                Assert.That(result.MappingSourcePath, Is.EqualTo(second.MappingSourcePath));
            });
        }

        public static void Delete_AfterSave_GetReturnsNull(IConfigProvider provider)
        {
            provider.SaveServerConfig(FullyPopulatedConfig());

            provider.DeleteServerConfig();

            Assert.That(provider.GetServerConfig(), Is.Null);
        }

        public static void Delete_WhenNothingSaved_IsANoOp(IConfigProvider provider)
        {
            Assert.DoesNotThrow(() => provider.DeleteServerConfig());
            Assert.That(provider.GetServerConfig(), Is.Null);
        }

        // Non-default values on every field, so a round-trip proves each one is
        // persisted rather than falling back to a ServerConfig default.
        private static ServerConfig FullyPopulatedConfig() => new ServerConfig
        {
            Url = "https://inventree.example.com",
            ApiKey = "contract-api-key",
            MappingSourcePath = @"\\server\share\mapping.json",
            BomKeyword = "contract-bom",
            WaitForServerAssignedIpn = false,
        };
    }
}
