using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Core
{
    public interface IMessageBus
    {
        Task PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage;
    }

    internal class DefaultMessageBus : IMessageBus
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DefaultMessageBus> _logger;
        
        public DefaultMessageBus(IServiceProvider serviceProvider, ILogger<DefaultMessageBus> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PublishAsync<TM>(TM message, CancellationToken cancellationToken = default) where TM : IMessage
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var publisher = _serviceProvider.GetService<IPublisher<TM>>();
            if (null == publisher)
            {
                _logger.LogWarning($"no publisher found for message type '{typeof(TM).FullName}'");
                return;
            }
            
            await publisher.PublishAsync(message, cancellationToken)
                            .ConfigureAwait(false);
        }
    }
}