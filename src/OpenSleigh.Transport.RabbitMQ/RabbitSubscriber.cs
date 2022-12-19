using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OpenSleigh.Transport.RabbitMQ
{
    public class RabbitSubscriber : ISubscriber
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly ISagaDescriptorsResolver _resolver;
        private readonly IServiceProvider _sp;
        private readonly List<IRabbitMessageSubscriber> _subscribers = new();

        public RabbitSubscriber(
            IHostApplicationLifetime hostApplicationLifetime, 
            ISagaDescriptorsResolver resolver, 
            IServiceProvider sp)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _sp = sp ?? throw new ArgumentNullException(nameof(sp));

            _hostApplicationLifetime = hostApplicationLifetime ?? throw new ArgumentNullException(nameof(hostApplicationLifetime));
            _hostApplicationLifetime.ApplicationStarted.Register(this.OnApplicationStarted);            
        }

        private void OnApplicationStarted()
        {
            var subscriberTypeBase = typeof(IRabbitMessageSubscriber<>);
            var messageTypes = _resolver.GetRegisteredMessageTypes();
            foreach (var messageType in messageTypes)
            {
                var subscriberType = subscriberTypeBase.MakeGenericType(messageType);
                var subscriber = (IRabbitMessageSubscriber)_sp.GetRequiredService(subscriberType);
                subscriber.Start();
                _subscribers.Add(subscriber);
            }
        }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            foreach (var subscriber in _subscribers)
                subscriber.Stop();
            return ValueTask.CompletedTask;
        }
    }
}