using System.Net.Http;
using NUnit.Framework;
using SwInventreeAddin.InvenTree;

namespace SwInventreeAddin.Tests
{
    [TestFixture]
    public class InventreeClientCreatePartValidationErrorServiceTests
    {
        private InventreeClientCreatePartValidationErrorService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _service = new InventreeClientCreatePartValidationErrorService();
        }

        [Test]
        public void ExtractIpnError_JsonWithIpnArray_ReturnsJoinedErrors()
        {
            // InvenTree keys field errors by the serializer field name: "IPN".
            const string body = @"{""IPN"": [""Part with this IPN already exists."", ""IPN is required.""]}";
            var message = $"InvenTree API returned 400 BadRequest: {body}";

            var result = _service.ExtractIpnError(message);

            Assert.That(result, Is.EqualTo("Part with this IPN already exists. IPN is required."));
        }

        [Test]
        public void ExtractIpnError_SingleIpnError_ReturnsError()
        {
            const string body = @"{""IPN"": [""Part with this IPN already exists.""]}";
            var message = $"InvenTree API returned 400 BadRequest: {body}";

            var result = _service.ExtractIpnError(message);

            Assert.That(result, Is.EqualTo("Part with this IPN already exists."));
        }

        [Test]
        public void ExtractIpnError_LowercaseIpnKey_StillMatched()
        {
            // Defensive: accept the lowercase key too in case a proxy or older
            // server version emits it.
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
            const string body = @"{""IPN"": [""Duplicate IPN.""]}";
            var ex = new HttpRequestException($"Request failed: {body}");

            var result = _service.ExtractIpnError(ex.Message);

            Assert.That(result, Is.EqualTo("Duplicate IPN."));
        }
    }
}
