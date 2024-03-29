﻿using OpenSleigh.Transport;
using OpenSleigh.Utils;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("OpenSleigh.Tests")]
namespace OpenSleigh
{
    internal class SagaDescriptorsResolver : ISagaDescriptorsResolver
    {
        private readonly ConcurrentDictionary<Type, ICollection<SagaDescriptor>> _descriptorsByMessage = new();
        private ITypeResolver _typeResolver;

        public SagaDescriptorsResolver(ITypeResolver typeResolver)
        {
            _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        }

        /// <inheritdoc/>
        public IEnumerable<Type> GetRegisteredMessageTypes()
            => _descriptorsByMessage.Keys;

        /// <inheritdoc/>
        public IEnumerable<SagaDescriptor> Resolve(IMessage message)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            var messageType = message.GetType();

            _descriptorsByMessage.TryGetValue(messageType, out var types);
            return types ?? Enumerable.Empty<SagaDescriptor>();
        }

        /// <inheritdoc/>
        public void Register<TS, TD>() 
            where TD : new()
            where TS : ISaga<TD>
        {            
            var descriptor = SagaDescriptor.Create<TS, TD>();

            Register(descriptor);
        }

        /// <inheritdoc/>
        public void Register<TS>() where TS : ISaga
        {
            var descriptor = SagaDescriptor.Create<TS>();
            Register(descriptor);
        }

        private void Register(SagaDescriptor descriptor)
        {
            var messageTypes = descriptor.SagaType.GetHandledMessageTypes();
            foreach (var messageType in messageTypes)
            {
                Register(messageType, descriptor);
            }
        }

        private void Register(Type messageType, SagaDescriptor descriptor)
        {
            _typeResolver.Register(messageType);

            _descriptorsByMessage.AddOrUpdate(messageType,
                (k) =>
                {
                    var res = new List<SagaDescriptor> { descriptor };
                    return res;
                },
                (k, v) =>
                {
                    v.Add(descriptor);
                    return v;
                });
        }

      
    }
}