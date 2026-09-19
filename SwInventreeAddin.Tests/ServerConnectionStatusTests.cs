using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    // The status card merges two axes — what is saved × the last probe outcome —
    // into the title, dot indicator, and the three fixed lines the window renders.
    [TestFixture]
    public class ServerConnectionStatusTests
    {
        private static ServerConfig SavedConfig(string url = "https://inventree.example.com",
                                                string apiKey = "saved-key") =>
            new ServerConfig { Url = url, ApiKey = apiKey };

        private static ConnectionProbeResult Probe(ConnectionProbeStatus status,
                                                 string message = "probe detail") =>
            new ConnectionProbeResult(status, message);

        // ── Saved axis ──────────────────────────────────────────────────────

        [Test]
        public void From_NoSavedConfig_IsNotSaved()
        {
            var status = ServerConnectionStatus.From(null);

            Assert.That(status.IsSaved, Is.False);
        }

        [Test]
        public void From_BlankUrl_IsNotSaved()
        {
            var status = ServerConnectionStatus.From(SavedConfig(url: "   "));

            Assert.That(status.IsSaved, Is.False);
        }

        [Test]
        public void From_SavedUrlWithoutKey_IsSavedButNotComplete()
        {
            var status = ServerConnectionStatus.From(SavedConfig(apiKey: string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(status.IsSaved, Is.True);
                Assert.That(status.IsComplete, Is.False);
            });
        }

        [Test]
        public void From_SavedUrlAndKey_IsComplete()
        {
            var status = ServerConnectionStatus.From(SavedConfig());

            Assert.That(status.IsComplete, Is.True);
        }

        // ── Title and indicator per state ───────────────────────────────────

        [Test]
        public void From_SavedUrlWithoutKey_RequiresAuthentication()
        {
            var status = ServerConnectionStatus.From(SavedConfig(apiKey: string.Empty));

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator,
                            Is.EqualTo(ServerConnectionIndicator.AuthenticationRequired));
                Assert.That(status.Title, Is.EqualTo("Authentication required"));
            });
        }

        [Test]
        public void From_CompleteConfigWithoutProbe_IsNotTested()
        {
            var status = ServerConnectionStatus.From(SavedConfig());

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator, Is.EqualTo(ServerConnectionIndicator.NotTested));
                Assert.That(status.Title, Is.EqualTo("Not tested"));
            });
        }

        [Test]
        public void From_ProbeInFlight_IsTesting()
        {
            var status = ServerConnectionStatus.From(SavedConfig(), probeInFlight: true);

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator, Is.EqualTo(ServerConnectionIndicator.Testing));
                Assert.That(status.Title, Is.EqualTo("Testing connection\u2026"));
            });
        }

        [Test]
        public void From_ProbeInFlight_WinsOverLastProbe()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.Connected), probeInFlight: true);

            Assert.That(status.Indicator, Is.EqualTo(ServerConnectionIndicator.Testing));
        }

        [Test]
        public void From_ConnectedProbe_IsConnected()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.Connected));

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator, Is.EqualTo(ServerConnectionIndicator.Connected));
                Assert.That(status.Title, Is.EqualTo("Connected"));
            });
        }

        [TestCase(ConnectionProbeStatus.Unreachable)]
        [TestCase(ConnectionProbeStatus.ServerError)]
        public void From_FailedProbe_IsConnectionFailed(ConnectionProbeStatus probeStatus)
        {
            var status = ServerConnectionStatus.From(SavedConfig(), Probe(probeStatus));

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator, Is.EqualTo(ServerConnectionIndicator.Failed));
                Assert.That(status.Title, Is.EqualTo("Connection failed"));
            });
        }

        [Test]
        public void From_RejectedCredential_RequiresAuthentication()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.CredentialRejected));

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator,
                            Is.EqualTo(ServerConnectionIndicator.AuthenticationRequired));
                Assert.That(status.Title, Is.EqualTo("Authentication required"));
            });
        }

        [Test]
        public void From_NotConfiguredProbe_IsUnconfiguredNotFailed()
        {
            // A clear-URL save returns NotConfigured — "saved OK with no URL,
            // nothing probed" (#253). It is not a failure: the status must
            // render the unconfigured state exactly as a blank URL does.
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.NotConfigured));

            Assert.Multiple(() =>
            {
                Assert.That(status.IsSaved, Is.False);
                Assert.That(status.IsComplete, Is.False);
                Assert.That(status.Indicator,
                            Is.EqualTo(ServerConnectionIndicator.NotTested));
                Assert.That(status.Title, Is.EqualTo("Not tested"));
                Assert.That(status.ServerLine, Is.EqualTo("not saved"));
                Assert.That(status.CredentialLine, Is.EqualTo("API key saved"),
                            "the key survives a URL clear \u2014 the line stays accurate");
                Assert.That(status.ConnectionLine, Is.EqualTo("\u2014"));
            });
        }

        [Test]
        public void From_NotProbedResult_RendersNoVerdictNotFailed()
        {
            // #260: the skip-probe save returns a no-verdict result — the card
            // must render it exactly like "no probe yet", never as a failure.
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.NotProbed));

            Assert.Multiple(() =>
            {
                Assert.That(status.Indicator,
                            Is.EqualTo(ServerConnectionIndicator.NotTested));
                Assert.That(status.Indicator,
                            Is.Not.EqualTo(ServerConnectionIndicator.Failed),
                            "a no-verdict result must never render the failure dot");
                Assert.That(status.Title, Is.EqualTo("Not tested"));
                Assert.That(status.IsComplete, Is.True);
                Assert.That(status.ConnectionLine, Is.EqualTo("not tested yet"));
            });
        }

        [Test]
        public void From_MissingKey_WinsOverLastProbe()
        {
            // A stale probe result must not hide that the credential is gone —
            // this is the landing state after Remove API key.
            var status = ServerConnectionStatus.From(
                SavedConfig(apiKey: string.Empty), Probe(ConnectionProbeStatus.Connected));

            Assert.That(status.Indicator,
                        Is.EqualTo(ServerConnectionIndicator.AuthenticationRequired));
        }

        // ── The three card lines ────────────────────────────────────────────

        [Test]
        public void From_SavedUrl_ServerLineShowsTrimmedUrl()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(url: "  https://inventree.example.com  "));

            Assert.That(status.ServerLine, Is.EqualTo("https://inventree.example.com"));
        }

        [Test]
        public void From_KeySaved_CredentialLineSaysApiKeySaved()
        {
            var status = ServerConnectionStatus.From(SavedConfig());

            Assert.That(status.CredentialLine, Is.EqualTo("API key saved"));
        }

        [Test]
        public void From_NoKeySaved_CredentialLineSaysNoneSaved()
        {
            var status = ServerConnectionStatus.From(SavedConfig(apiKey: string.Empty));

            Assert.That(status.CredentialLine, Is.EqualTo("none saved"));
        }

        [Test]
        public void From_NotComplete_ConnectionLineIsBlank()
        {
            var status = ServerConnectionStatus.From(SavedConfig(apiKey: string.Empty));

            Assert.That(status.ConnectionLine, Is.EqualTo("\u2014"));
        }

        [Test]
        public void From_CompleteWithoutProbe_ConnectionLineSaysNotTestedYet()
        {
            var status = ServerConnectionStatus.From(SavedConfig());

            Assert.That(status.ConnectionLine, Is.EqualTo("not tested yet"));
        }

        [Test]
        public void From_ProbeInFlight_ConnectionLineSaysTesting()
        {
            var status = ServerConnectionStatus.From(SavedConfig(), probeInFlight: true);

            Assert.That(status.ConnectionLine, Is.EqualTo("testing\u2026"));
        }

        [Test]
        public void From_ConnectedProbe_ConnectionLineSaysLastTestSucceeded()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.Connected));

            Assert.That(status.ConnectionLine, Is.EqualTo("last test succeeded"));
        }

        [TestCase(ConnectionProbeStatus.Unreachable)]
        [TestCase(ConnectionProbeStatus.ServerError)]
        [TestCase(ConnectionProbeStatus.CredentialRejected)]
        public void From_FailedProbe_ConnectionLineCarriesProbeMessage(
            ConnectionProbeStatus probeStatus)
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(),
                Probe(probeStatus, "The server rejected the API key (401 Unauthorized)."));

            Assert.That(status.ConnectionLine,
                        Is.EqualTo("The server rejected the API key (401 Unauthorized)."));
        }

        // ── ADR-0022: no produced string ever contains the API key ─────────

        [Test]
        public void From_AnyState_NoProducedStringContainsTheApiKey()
        {
            var status = ServerConnectionStatus.From(
                SavedConfig(apiKey: "super-secret-key"),
                Probe(ConnectionProbeStatus.CredentialRejected, "rejected"));

            Assert.Multiple(() =>
            {
                Assert.That(status.Title, Does.Not.Contain("super-secret-key"));
                Assert.That(status.ServerLine, Does.Not.Contain("super-secret-key"));
                Assert.That(status.CredentialLine, Does.Not.Contain("super-secret-key"));
                Assert.That(status.ConnectionLine, Does.Not.Contain("super-secret-key"));
            });
        }

        // ── Value equality — the #241 event gates on a real state move ──

        [Test]
        public void Equals_IdenticalValues_AreEqual()
        {
            var a = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.Connected));
            var b = ServerConnectionStatus.From(
                SavedConfig(), Probe(ConnectionProbeStatus.Connected));

            Assert.Multiple(() =>
            {
                Assert.That(a.Equals(b), Is.True);
                Assert.That(a.Equals((object)b), Is.True);
                Assert.That(a, Is.EqualTo(b));
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()),
                            "equal values must hash alike — dictionary lookups depend on it");
            });
        }

        // Every member feeds equality: a status that differs in the named
        // member is not equal to the baseline. From couples some members —
        // a missing key also moves Indicator/Title — so the named member is
        // the one guaranteed to differ, not necessarily the only one.
        [TestCase(nameof(ServerConnectionStatus.IsSaved))]
        [TestCase(nameof(ServerConnectionStatus.IsComplete))]
        [TestCase(nameof(ServerConnectionStatus.Indicator))]
        [TestCase(nameof(ServerConnectionStatus.Title))]
        [TestCase(nameof(ServerConnectionStatus.ServerLine))]
        [TestCase(nameof(ServerConnectionStatus.CredentialLine))]
        [TestCase(nameof(ServerConnectionStatus.ConnectionLine))]
        public void Equals_StatusDifferingInMember_IsNotEqual(string member)
        {
            var baseline = ServerConnectionStatus.From(SavedConfig());
            var other = StatusDifferingIn(member);

            Assert.Multiple(() =>
            {
                Assert.That(baseline.Equals(other), Is.False);
                Assert.That(baseline, Is.Not.EqualTo(other));
            });
        }

        [Test]
        public void Equals_NullAndForeignType_AreNotEqual()
        {
            var status = ServerConnectionStatus.From(SavedConfig());

            ServerConnectionStatus? nullStatus = null;

            Assert.Multiple(() =>
            {
                Assert.That(status.Equals(nullStatus), Is.False);
                Assert.That(status.Equals((object?)nullStatus), Is.False);
                Assert.That(status.Equals((object)"not a status"), Is.False);
                Assert.That(status, Is.Not.EqualTo(null));
            });
        }

        private static ServerConnectionStatus StatusDifferingIn(string member)
        {
            switch (member)
            {
                case nameof(ServerConnectionStatus.IsSaved):
                    return ServerConnectionStatus.From(null);
                case nameof(ServerConnectionStatus.IsComplete):
                case nameof(ServerConnectionStatus.CredentialLine):
                    return ServerConnectionStatus.From(SavedConfig(apiKey: string.Empty));
                case nameof(ServerConnectionStatus.Indicator):
                    return ServerConnectionStatus.From(SavedConfig(), probeInFlight: true);
                case nameof(ServerConnectionStatus.Title):
                    return ServerConnectionStatus.From(
                        SavedConfig(), Probe(ConnectionProbeStatus.Connected));
                case nameof(ServerConnectionStatus.ServerLine):
                    return ServerConnectionStatus.From(
                        SavedConfig(url: "https://other.example.com"));
                case nameof(ServerConnectionStatus.ConnectionLine):
                    return ServerConnectionStatus.From(
                        SavedConfig(), Probe(ConnectionProbeStatus.Unreachable));
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(member), member, null);
            }
        }
    }
}
