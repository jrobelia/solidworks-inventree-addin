using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SwInventreeAddin.Tests
{
    /// <summary>
    /// The shared exception→user-text extraction (#354): transport envelopes
    /// surface their innermost non-empty message so the status strip can tell
    /// "connection refused" from "remote name could not be resolved" from a
    /// timeout; every other exception type keeps its own message verbatim so
    /// already-actionable wraps survive untouched.
    /// </summary>
    [TestFixture]
    public class ExceptionTextTests
    {
        [Test]
        public void ForUser_EnvelopeWithInnerChain_ReturnsInnermostMessage()
        {
            var ex = new HttpRequestException(
                "An error occurred while sending the request.",
                new Exception("mid", new Exception("socket-level cause")));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("socket-level cause"));
        }

        [Test]
        public void ForUser_EnvelopeWithoutInner_ReturnsOwnMessage()
        {
            // The API-status throw carries its detail on the envelope itself —
            // there is no transport layer beneath it to walk to.
            var ex = new HttpRequestException("InvenTree API returned 404");

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("InvenTree API returned 404"));
        }

        [Test]
        public void ForUser_EnvelopeWithEmptyInnermost_ReturnsNextNonEmptyOutward()
        {
            var ex = new HttpRequestException(
                "outer",
                new Exception("mid", new Exception(string.Empty)));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("mid"));
        }

        [Test]
        public void ForUser_EnvelopeWithAllEmptyMessages_ReturnsEmpty()
        {
            var ex = new HttpRequestException(
                string.Empty,
                new Exception(string.Empty));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo(string.Empty));
        }

        [Test]
        public void ForUser_WebExceptionEnvelope_ReturnsInnermostMessage()
        {
            var ex = new WebException("outer", new Exception("socket-level cause"));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("socket-level cause"));
        }

        [Test]
        public void ForUser_TaskCanceledEnvelope_ReturnsInnermostMessage()
        {
            var ex = new TaskCanceledException("outer", new Exception("the operation timed out"));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("the operation timed out"));
        }

        [Test]
        public void ForUser_OperationCanceledEnvelope_ReturnsInnermostMessage()
        {
            var ex = new OperationCanceledException("outer", new Exception("timed out"));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("timed out"));
        }

        [Test]
        public void ForUser_SocketExceptionEnvelope_ReturnsOwnMessage()
        {
            // SocketException cannot carry an inner (no (message, inner) ctor);
            // a bare one still reports its own message rather than nothing.
            var ex = new SocketException(10061);

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo(ex.Message));
        }

        [Test]
        public void ForUser_NonEnvelopeWithInner_ReturnsOuterMessage()
        {
            // Discriminating fixture: the wrap already says the actionable
            // thing (e.g. the token service's "Could not reach…") — an
            // inner-exception walk triggered by "has inner" rather than
            // "is envelope type" would strip it to a bare socket message.
            var ex = new InvalidOperationException("outer", new Exception("inner"));

            Assert.That(ExceptionText.ForUser(ex), Is.EqualTo("outer"));
        }

        [Test]
        public void ForUser_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ExceptionText.ForUser(null!));
        }
    }
}
