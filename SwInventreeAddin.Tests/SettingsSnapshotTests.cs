using NUnit.Framework;
using SwInventreeAddin.UI;

namespace SwInventreeAddin.Tests
{
    // The single home of the dirty-gating rules: Apply/Save enable only when a
    // persistable change exists. Credential fields count only when they hold a
    // complete credential — a typed key draft or a full username+password pair;
    // blank or half-typed credential fields never count.
    [TestFixture]
    public class SettingsSnapshotTests
    {
        private static SettingsSnapshot Saved() =>
            new SettingsSnapshot(
                url: "https://inventree.example.com",
                apiKeyDraft: string.Empty,
                hasSavedApiKey: true,
                username: string.Empty,
                password: string.Empty,
                sharedPath: string.Empty,
                bomKeyword: "inventree",
                useLocalMapping: true,
                waitForServerAssignedIpn: true);

        private static SettingsSnapshot Current(
            string url = "https://inventree.example.com",
            string apiKeyDraft = "",
            bool hasSavedApiKey = true,
            string username = "",
            string password = "",
            string sharedPath = "",
            string bomKeyword = "inventree",
            bool useLocalMapping = true,
            bool waitForServerAssignedIpn = true) =>
            new SettingsSnapshot(
                url, apiKeyDraft, hasSavedApiKey, username, password,
                sharedPath, bomKeyword, useLocalMapping, waitForServerAssignedIpn);

        [Test]
        public void HasPersistableChangeFrom_IdenticalValues_IsFalse()
        {
            Assert.That(Current().HasPersistableChangeFrom(Saved()), Is.False);
        }

        [Test]
        public void HasPersistableChangeFrom_UrlChanged_IsTrue()
        {
            Assert.That(
                Current(url: "https://other.example.com").HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_KeyDraftTyped_IsTrue()
        {
            Assert.That(
                Current(apiKeyDraft: "inv-new").HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_WhitespaceKeyDraft_IsFalse()
        {
            Assert.That(
                Current(apiKeyDraft: "   ").HasPersistableChangeFrom(Saved()),
                Is.False);
        }

        [Test]
        public void HasPersistableChangeFrom_SavedKeyRemoved_IsTrue()
        {
            Assert.That(
                Current(hasSavedApiKey: false).HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_CompletePair_IsTrue()
        {
            Assert.That(
                Current(username: "engineer", password: "s3cret")
                    .HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_UsernameOnly_IsFalse()
        {
            Assert.That(
                Current(username: "engineer").HasPersistableChangeFrom(Saved()),
                Is.False);
        }

        [Test]
        public void HasPersistableChangeFrom_PasswordOnly_IsFalse()
        {
            Assert.That(
                Current(password: "s3cret").HasPersistableChangeFrom(Saved()),
                Is.False);
        }

        [Test]
        public void HasPersistableChangeFrom_WhitespacePassword_IsFalse()
        {
            Assert.That(
                Current(username: "engineer", password: "   ")
                    .HasPersistableChangeFrom(Saved()),
                Is.False);
        }

        [Test]
        public void HasPersistableChangeFrom_SharedPathChanged_IsTrue()
        {
            Assert.That(
                Current(sharedPath: "\\\\share\\mapping.json", useLocalMapping: false)
                    .HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_BomKeywordChanged_IsTrue()
        {
            Assert.That(
                Current(bomKeyword: "custom").HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_UseLocalMappingChanged_IsTrue()
        {
            Assert.That(
                Current(useLocalMapping: false).HasPersistableChangeFrom(Saved()),
                Is.True);
        }

        [Test]
        public void HasPersistableChangeFrom_WaitFlagChanged_IsTrue()
        {
            Assert.That(
                Current(waitForServerAssignedIpn: false).HasPersistableChangeFrom(Saved()),
                Is.True);
        }
    }
}
