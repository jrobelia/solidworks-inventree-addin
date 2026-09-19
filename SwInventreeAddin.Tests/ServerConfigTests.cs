using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class ServerConfigTests
    {
        // #259: the property initializer is the declared default — every
        // null-config fallback and DTO/ViewModel initializer folds into the
        // same const, so a default change cannot leave stale literals behind.
        [Test]
        public void WaitForServerAssignedIpn_NewConfig_MatchesDeclaredDefault()
        {
            Assert.That(new ServerConfig().WaitForServerAssignedIpn,
                        Is.EqualTo(ServerConfig.DefaultWaitForServerAssignedIpn));
        }
    }
}
