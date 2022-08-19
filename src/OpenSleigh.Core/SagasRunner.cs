using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public class SagasRunner : ISagasRunner
    {
        private readonly ISagaRunnersFactory _runnersFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        public SagasRunner(ISagaRunnersFactory runnersFactory, IServiceScopeFactory scopeFactory)
        {
            _runnersFactory = runnersFactory ?? throw new ArgumentNullException(nameof(runnersFactory));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        }

        public Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (messageContext == null)
                throw new ArgumentNullException(nameof(messageContext));

            return RunAsyncCore(messageContext, cancellationToken);
        }

        private async Task RunAsyncCore<TM>(IMessageContext<TM> messageContext,
                                            CancellationToken cancellationToken) where TM : IMessage
        {
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var runners = _runnersFactory.Create<TM>(scope);
                if (null == runners)
                    return;

                var exceptions = new List<Exception>();

                foreach (var runner in runners)
                {
                    try
                    {
                        await runner.RunAsync(messageContext, cancellationToken);
                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e);
                    }
                }

                if (exceptions.Any())
                    throw new AggregateException(exceptions);
            }
        }
    }
}