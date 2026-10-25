using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace SwInventreeAddin
{
    /// <summary>
    /// Exception → user-facing failure text, shared by every surface that
    /// would otherwise read <see cref="Exception.Message"/> raw (#354). A
    /// transport envelope (<see cref="HttpRequestException"/>,
    /// <see cref="WebException"/>, <see cref="SocketException"/>,
    /// <see cref="OperationCanceledException"/>) reports the innermost
    /// non-empty message — the socket-level cause that distinguishes
    /// server-down from wrong-URL from timeout — while any other exception
    /// keeps its own message, so already-actionable wraps (the token
    /// service's "Could not reach…", domain validation errors) survive
    /// untouched. No wording is composed here; callers keep their prefixes.
    /// </summary>
    internal static class ExceptionText
    {
        /// <summary>
        /// Returns the message to show for <paramref name="exception"/>:
        /// the innermost non-empty <see cref="Exception.Message"/> for a
        /// transport envelope, the exception's own message otherwise.
        /// </summary>
        public static string ForUser(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (!IsTransportEnvelope(exception))
                return exception.Message;

            var chain = new List<Exception>();
            for (var current = exception; current != null; current = current.InnerException)
                chain.Add(current);

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(chain[i].Message))
                    return chain[i].Message;
            }
            return string.Empty;
        }

        private static bool IsTransportEnvelope(Exception exception) =>
            exception is HttpRequestException
            || exception is WebException
            || exception is SocketException
            || exception is OperationCanceledException;
    }
}
