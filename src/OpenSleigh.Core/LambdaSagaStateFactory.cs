using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    internal class LambdaSagaStateFactory<TM, TD> : ISagaStateFactory<TM, TD>
        where TM : IMessage
        where TD : SagaState
    {
        private readonly Func<TM, TD> _factory;

        public LambdaSagaStateFactory(Func<TM, TD> factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public TD Create(TM message) => _factory(message);

        public TD Create(IMessage message)
        {
            if (message is TM m)
                return _factory(m);
            return null;
        } 
    }
}