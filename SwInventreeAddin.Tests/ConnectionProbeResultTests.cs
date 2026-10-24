using System;
using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    // A probe that throws before producing a verdict — an unparsable saved URL,
    // a credential the token service cannot resolve — surfaces as an Unreachable
    // verdict through the one factory every probe caller shares (#234, #241).
    [TestFixture]
    public class ConnectionProbeResultTests
    {
        [Test]
        public void UnreachableFromException_CarriesMessageAsUnreachableVerdict()
        {
            var result = ConnectionProbeResult.UnreachableFromException(
                new InvalidOperationException("The saved server URL could not be parsed."));

            Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(ConnectionProbeStatus.Unreachable));
                Assert.That(result.Message, Is.EqualTo("The saved server URL could not be parsed."));
                Assert.That(result.Succeeded, Is.False);
            });
        }
    }
}
