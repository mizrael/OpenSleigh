using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;
using System.Collections.Generic;

namespace OpenSleigh.Core
{
    public interface ISagaRunnersFactory
    {
        /// <summary>
        /// creates the runners capable of handling messages of type <typeparamref name="TM"/>.
        /// </summary>
        /// <typeparam name="TM"></typeparam>
        /// <returns></returns>
        IEnumerable<ISagaRunner> Create<TM>(IServiceScope scope) where TM : IMessage;
    }
}