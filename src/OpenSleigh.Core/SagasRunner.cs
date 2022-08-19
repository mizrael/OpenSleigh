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
            _scopeFactory = scopeFactory;
        }

        public Task RunAsync<TM>(IMessageContext<TM> messageContext, CancellationToken cancellationToken = default)
            where TM : IMessage
        {
            if (messageContext == null)
                throw new ArgumentNullException(nameof(messageContext));

            using (var scope = _scopeFactory.CreateScope())
            {
                var runners = _runnersFactory.Create<TM>(scope);
                if (null == runners)
                    return Task.CompletedTask;

                return RunAsyncCore(messageContext, runners, cancellationToken);
            }
        }

        private async Task RunAsyncCore<TM>(IMessageContext<TM> messageContext,
            IEnumerable<ISagaRunner> runners, 
            CancellationToken cancellationToken) where TM : IMessage
        {
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