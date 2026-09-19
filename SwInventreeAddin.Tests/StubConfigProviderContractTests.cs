using NUnit.Framework;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    // The stub adapter runs the same IConfigProvider contract as
    // EncryptedConfigProvider — a stub that merely records calls (the #232
    // bug, where a save followed by a read returned the pre-save state)
    // fails here instead of distorting the code under test.
    [TestFixture]
    public class StubConfigProviderContractTests
    {
        [Test]
        public void Get_WhenNothingSaved_ReturnsNull() =>
            ConfigProviderContract.Get_WhenNothingSaved_ReturnsNull(
                StubConfigProvider.WithNoSavedConfig());

        [Test]
        public void SaveThenGet_RoundTripsSavedValues() =>
            ConfigProviderContract.SaveThenGet_RoundTripsSavedValues(
                StubConfigProvider.WithNoSavedConfig());

        [Test]
        public void Save_Overwrite_LastWriteWins() =>
            ConfigProviderContract.Save_Overwrite_LastWriteWins(
                StubConfigProvider.WithNoSavedConfig());

        [Test]
        public void Delete_AfterSave_GetReturnsNull() =>
            ConfigProviderContract.Delete_AfterSave_GetReturnsNull(
                StubConfigProvider.WithNoSavedConfig());

        [Test]
        public void Delete_WhenNothingSaved_IsANoOp() =>
            ConfigProviderContract.Delete_WhenNothingSaved_IsANoOp(
                StubConfigProvider.WithNoSavedConfig());
    }
}
