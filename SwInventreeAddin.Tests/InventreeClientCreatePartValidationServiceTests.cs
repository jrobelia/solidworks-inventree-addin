using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;
using SwInventreeAddin.Tests.Stubs;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeClientCreatePartValidationServiceTests
    {
        private StubInventreeClient _client = null!;
        private InventreeClientCreatePartValidationService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _client  = new StubInventreeClient();
            _service = new InventreeClientCreatePartValidationService(_client);
        }

        [Test]
        public async Task CheckIpnAvailableAsync_ExistingPart_ReturnsUnavailable()
        {
            _client.PartToReturn = new InventreePart { Pk = 1, Ipn = "DUP-001", Name = "Existing" };

            var result = await _service.CheckIpnAvailableAsync("DUP-001");

            Assert.That(result.IsAvailable, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("DUP-001").And.Contain("already exists").IgnoreCase);
            Assert.That(_client.LastIpnRequested, Is.EqualTo("DUP-001"));
        }

        [Test]
        public async Task CheckIpnAvailableAsync_NoExistingPart_ReturnsAvailable()
        {
            _client.PartToReturn = null;

            var result = await _service.CheckIpnAvailableAsync("NEW-001");

            Assert.That(result.IsAvailable, Is.True);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(_client.LastIpnRequested, Is.EqualTo("NEW-001"));
        }

        [Test]
        public async Task CheckIpnAvailableAsync_WhitespaceIpn_ReturnsAvailable()
        {
            _client.PartToReturn = new InventreePart { Pk = 1, Ipn = "DUP-001", Name = "Existing" };

            var result = await _service.CheckIpnAvailableAsync("   ");

            Assert.That(result.IsAvailable, Is.True);
            Assert.That(_client.LastIpnRequested, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ExtractIpnError_JsonWithIpnArray_ReturnsJoinedErrors()
        {
            const string body = @"{""ipn"": [""Part with this IPN already exists."", ""IPN is required.""]}";
            var message = $"InvenTree API returned 400 BadRequest: {body}";

            var result = _service.ExtractIpnError(message);

            Assert.That(result, Is.EqualTo("Part with this IPN already exists. IPN is required."));
        }

        [Test]
        public void ExtractIpnError_SingleIpnError_ReturnsError()
        {
            const string body = @"{""ipn"": [""Part with this IPN already exists.""]}";
            var message = $"InvenTree API returned 400 BadRequest: {body}";

            var result = _service.ExtractIpnError(message);

            Assert.That(result, Is.EqualTo("Part with this IPN already exists."));
        }

        [Test]
        public void ExtractIpnError_NoJson_ReturnsNull()
        {
            var result = _service.ExtractIpnError("Generic network failure");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIpnError_MalformedJson_ReturnsNull()
        {
            var result = _service.ExtractIpnError("BadRequest: {not valid json");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIpnError_JsonWithoutIpnField_ReturnsNull()
        {
            const string body = @"{""name"": [""Name is required.""]}";
            var message = $"InvenTree API returned 400 BadRequest: {body}";

            var result = _service.ExtractIpnError(message);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ExtractIpnError_ExceptionWithHttpRequestException_MessageExtracted()
        {
            const string body = @"{""ipn"": [""Duplicate IPN.""]}";
            var ex = new HttpRequestException($"Request failed: {body}");

            var result = _service.ExtractIpnError(ex.Message);

            Assert.That(result, Is.EqualTo("Duplicate IPN."));
        }
    }
}
