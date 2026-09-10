using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class CredentialEditorStateTests
    {
        [Test]
        public void FromSavedConfig_NoSavedConfig_SelectsAccountMode()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.Account));
        }

        [Test]
        public void FromSavedConfig_SavedConfigWithApiKey_SelectsApiKeyMode()
        {
            var state = CredentialEditorState.FromSavedConfig(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.ApiKey));
        }

        [Test]
        public void FromSavedConfig_SavedConfigWithApiKey_SeedsApiKey()
        {
            var state = CredentialEditorState.FromSavedConfig(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.ApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public void FromSavedConfig_SavedConfigWithBlankApiKey_SelectsAccountMode()
        {
            var state = CredentialEditorState.FromSavedConfig(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "   " });

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.Account));
        }

        [Test]
        public void FromSavedConfig_SavedConfigWithBlankApiKey_LeavesApiKeyEmpty()
        {
            var state = CredentialEditorState.FromSavedConfig(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "   " });

            Assert.That(state.ApiKey, Is.Empty);
        }

        [Test]
        public void FromSavedConfig_SavedConfigWithApiKey_KeepsApiKeyMasked()
        {
            var state = CredentialEditorState.FromSavedConfig(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

        [Test]
        public void ToggleApiKeyReveal_WhenMasked_RevealsApiKey()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            state.ToggleApiKeyReveal();

            Assert.That(state.IsApiKeyRevealed, Is.True);
        }

        [Test]
        public void ToggleApiKeyReveal_WhenRevealed_MasksApiKeyAgain()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            state.ToggleApiKeyReveal();
            state.ToggleApiKeyReveal();

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

        [Test]
        public void SelectMode_WithApiKeyMode_ChangesMode()
        {
            var state = CredentialEditorState.FromSavedConfig(null);

            state.SelectMode(CredentialEntryMode.ApiKey);

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.ApiKey));
        }

        [Test]
        public void SelectMode_WhenApiKeyRevealed_MasksApiKeyAgain()
        {
            var state = CredentialEditorState.FromSavedConfig(null);
            state.ToggleApiKeyReveal();

            state.SelectMode(CredentialEntryMode.ApiKey);

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

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
    }
}
