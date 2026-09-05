using System;

namespace SwInventreeAddin.Config
{
    /// <summary>
    /// Encapsulates attaching and detaching a single callback from
    /// <see cref="IPropertyMappingProvider.MappingChanged"/> so multiple consumers
    /// don't repeat the same null-check, handler-cache, and unsubscribe pattern.
    /// </summary>
    public sealed class MappingChangedSubscription
    {
        private readonly IPropertyMappingProvider _provider;
        private readonly Action _callback;
        private          EventHandler? _handler;

        /// <summary>
        /// Creates a subscription for <paramref name="provider"/> that will invoke
        /// <paramref name="callback"/> whenever the mapping file changes.
        /// </summary>
        public MappingChangedSubscription(IPropertyMappingProvider provider, Action callback)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        /// <summary>
        /// Attaches the callback to <see cref="IPropertyMappingProvider.MappingChanged"/>.
        /// Safe to call multiple times; subsequent calls are ignored.
        /// </summary>
        public void Subscribe()
        {
            if (_handler != null)
                return;

            _handler = (s, e) => _callback();
            _provider.MappingChanged += _handler;
        }

        /// <summary>
        /// Detaches the callback from <see cref="IPropertyMappingProvider.MappingChanged"/>.
        /// Safe to call when not subscribed; subsequent calls are ignored.
        /// </summary>
        public void Unsubscribe()
        {
            if (_handler == null)
                return;

            _provider.MappingChanged -= _handler;
            _handler = null;
        }

        /// <summary>
        /// Unsubscribes and clears <paramref name="current"/>, then creates and subscribes
        /// a new subscription for <paramref name="provider"/>. Passing <c>null</c> simply
        /// unsubscribes and clears the field.
        /// </summary>
        public static void SubscribeTo(
            ref MappingChangedSubscription? current,
            IPropertyMappingProvider? provider,
            Action callback)
        {
            current?.Unsubscribe();

            if (provider == null)
            {
                current = null;
                return;
            }

            current = new MappingChangedSubscription(provider, callback);
            current.Subscribe();
        }

        /// <summary>
        /// Unsubscribes and clears <paramref name="current"/>.
        /// </summary>
        public static void UnsubscribeFrom(ref MappingChangedSubscription? current)
        {
            current?.Unsubscribe();
            current = null;
        }
    }
}
