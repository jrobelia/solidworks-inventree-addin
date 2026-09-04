using System.Net.Http;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeClientCreatePartValidationServiceTests
    {
        private InventreeClientCreatePartValidationService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _service = new InventreeClientCreatePartValidationService();
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
