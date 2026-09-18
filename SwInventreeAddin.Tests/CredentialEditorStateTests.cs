using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    // Write-once credential state: the saved key is never editable text — the
    // typed draft is the only editable value. ApplyCredentialTo encodes the
    // precedence a typed key draft > a complete username+password pair > the
    // saved key, so callers never re-implement it.
    [TestFixture]
    public class CredentialEditorStateTests
    {
        private static ServerConfig SavedWithKey(string key = "saved-key") =>
            new ServerConfig { Url = "https://inventree.example.com", ApiKey = key };

        // ── FromSavedConfig ─────────────────────────────────────────────────

        [Test]
        public void FromSavedConfig_NoSavedConfig_HasNoSavedApiKey()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            Assert.That(state.HasSavedApiKey, Is.False);
        }

        [Test]
        public void FromSavedConfig_SavedKey_ReportsHasSavedApiKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());

            Assert.That(state.HasSavedApiKey, Is.True);
        }

        [Test]
        public void FromSavedConfig_BlankSavedKey_HasNoSavedApiKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey("   "));

            Assert.That(state.HasSavedApiKey, Is.False);
        }

        [Test]
        public void FromSavedConfig_SavedKey_DraftIsNeverSeededWithIt()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());

            Assert.That(state.ApiKey, Is.Empty);
        }

        // ── ApiKey draft ────────────────────────────────────────────────────

        [Test]
        public void ApiKey_WhenEdited_ReturnsTheEditedValue()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            state.ApiKey = "typed-key";

            Assert.That(state.ApiKey, Is.EqualTo("typed-key"));
        }

        [Test]
        public void ApiKey_WhenSetToNull_ReturnsEmpty()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            state.ApiKey = null!;

            Assert.That(state.ApiKey, Is.Empty);
        }

        // ── ApplyCredentialTo: credential precedence ────────────────────────

        [Test]
        public void ApplyCredentialTo_KeyDraft_SetsRawApiKeyAndClearsThePair()
        {
            var state = CredentialEditorState.FromSavedConfig(null);
            state.ApiKey = "typed-key";
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "user", "pass");

            Assert.Multiple(() =>
            {
                Assert.That(input.RawApiKey, Is.EqualTo("typed-key"));
                Assert.That(input.Username, Is.Empty);
                Assert.That(input.Password, Is.Empty);
            });
        }

        [Test]
        public void ApplyCredentialTo_KeyDraftAndCompletePair_KeyWins()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            state.ApiKey = "typed-key";
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "engineer", "s3cret");

            Assert.Multiple(() =>
            {
                Assert.That(input.RawApiKey, Is.EqualTo("typed-key"),
                            "a filled API key takes precedence over a filled pair");
                Assert.That(input.Username, Is.Empty);
                Assert.That(input.Password, Is.Empty);
            });
        }

        [Test]
        public void ApplyCredentialTo_CompletePair_SetsUsernameAndPassword()
        {
            var state = CredentialEditorState.FromSavedConfig(null);
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "engineer", "s3cret");

            Assert.Multiple(() =>
            {
                Assert.That(input.Username, Is.EqualTo("engineer"));
                Assert.That(input.Password, Is.EqualTo("s3cret"));
                Assert.That(input.RawApiKey, Is.Empty);
            });
        }

        // A typed complete pair must log in — it overrides the saved key so the
        // resolved token replaces it on persist (seam-gate amendment).
        [Test]
        public void ApplyCredentialTo_CompletePair_OverridesSavedKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "engineer", "s3cret");

            Assert.Multiple(() =>
            {
                Assert.That(input.Username, Is.EqualTo("engineer"));
                Assert.That(input.Password, Is.EqualTo("s3cret"));
                Assert.That(input.RawApiKey, Is.Empty,
                            "the pair must not lose to the saved key");
            });
        }

        [Test]
        public void ApplyCredentialTo_UsernameOnly_FallsBackToSavedKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "engineer", string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
                Assert.That(input.Username, Is.Empty);
                Assert.That(input.Password, Is.Empty);
            });
        }

        [Test]
        public void ApplyCredentialTo_PasswordOnly_FallsBackToSavedKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, string.Empty, "s3cret");

            Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public void ApplyCredentialTo_NothingTyped_UsesSavedKey()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, string.Empty, string.Empty);

            Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public void ApplyCredentialTo_NothingTypedAndNothingSaved_LeavesEverythingEmpty()
        {
            var state = CredentialEditorState.FromSavedConfig(null);
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, string.Empty, string.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(input.RawApiKey, Is.Empty);
                Assert.That(input.Username, Is.Empty);
                Assert.That(input.Password, Is.Empty);
            });
        }

        [Test]
        public void ApplyCredentialTo_WhitespaceDraft_IsTreatedAsBlank()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            state.ApiKey = "   ";
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, string.Empty, string.Empty);

            Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public void ApplyCredentialTo_PairIsNotComplete_WhenPasswordIsWhitespace()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, "engineer", "   ");

            Assert.That(input.RawApiKey, Is.EqualTo("saved-key"));
        }

        // ── Clear ───────────────────────────────────────────────────────────

        [Test]
        public void Clear_DropsSavedKeyAndDraft()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            state.ApiKey = "typed-key";

            state.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(state.HasSavedApiKey, Is.False);
                Assert.That(state.ApiKey, Is.Empty);
            });
        }

        [Test]
        public void Clear_ThenApplyCredentialTo_HasNoSavedKeyToFallBackTo()
        {
            var state = CredentialEditorState.FromSavedConfig(SavedWithKey());
            state.Clear();
            var input = new SettingsApplyInput();

            state.ApplyCredentialTo(input, string.Empty, string.Empty);

            Assert.That(input.RawApiKey, Is.Empty);
        }
    }
}
