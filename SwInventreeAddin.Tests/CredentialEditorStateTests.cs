using NUnit.Framework;
using SwInventreeAddin.Config;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class CredentialEditorStateTests
    {
        [Test]
        public void For_NoSavedConfig_SelectsAccountMode()
        {
            var state = CredentialEditorState.For(null);

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.Account));
        }

        [Test]
        public void For_SavedConfigWithApiKey_SelectsApiKeyMode()
        {
            var state = CredentialEditorState.For(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.ApiKey));
        }

        [Test]
        public void For_SavedConfigWithApiKey_SeedsApiKey()
        {
            var state = CredentialEditorState.For(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.ApiKey, Is.EqualTo("saved-key"));
        }

        [Test]
        public void For_SavedConfigWithBlankApiKey_SelectsAccountMode()
        {
            var state = CredentialEditorState.For(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "   " });

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.Account));
        }

        [Test]
        public void For_SavedConfigWithBlankApiKey_LeavesApiKeyEmpty()
        {
            var state = CredentialEditorState.For(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "   " });

            Assert.That(state.ApiKey, Is.Empty);
        }

        [Test]
        public void For_SavedConfigWithApiKey_KeepsApiKeyMasked()
        {
            var state = CredentialEditorState.For(
                new ServerConfig { Url = "https://inventree.example.com", ApiKey = "saved-key" });

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

        [Test]
        public void ToggleApiKeyReveal_WhenMasked_RevealsApiKey()
        {
            var state = CredentialEditorState.For(null);

            state.ToggleApiKeyReveal();

            Assert.That(state.IsApiKeyRevealed, Is.True);
        }

        [Test]
        public void ToggleApiKeyReveal_WhenRevealed_MasksApiKeyAgain()
        {
            var state = CredentialEditorState.For(null);

            state.ToggleApiKeyReveal();
            state.ToggleApiKeyReveal();

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

        [Test]
        public void SelectMode_WithApiKeyMode_ChangesMode()
        {
            var state = CredentialEditorState.For(null);

            state.SelectMode(CredentialEntryMode.ApiKey);

            Assert.That(state.Mode, Is.EqualTo(CredentialEntryMode.ApiKey));
        }

        [Test]
        public void SelectMode_WhenApiKeyRevealed_MasksApiKeyAgain()
        {
            var state = CredentialEditorState.For(null);
            state.ToggleApiKeyReveal();

            state.SelectMode(CredentialEntryMode.ApiKey);

            Assert.That(state.IsApiKeyRevealed, Is.False);
        }

        [Test]
        public void ApiKey_WhenEdited_ReturnsTheEditedValue()
        {
            var state = CredentialEditorState.For(null);

            state.ApiKey = "typed-key";

            Assert.That(state.ApiKey, Is.EqualTo("typed-key"));
        }

        [Test]
        public void ApiKey_WhenSetToNull_ReturnsEmpty()
        {
            var state = CredentialEditorState.For(null);

            state.ApiKey = null!;

            Assert.That(state.ApiKey, Is.Empty);
        }
    }
}
