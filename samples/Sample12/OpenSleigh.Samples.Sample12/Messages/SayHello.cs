using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample12.Messages
{
    public record SayHello(Guid Id, Guid CorrelationId, string Name) : ICommand
    {
        public static SayHello Create(string name)
            => new SayHello(Id: Guid.NewGuid(), CorrelationId: Guid.NewGuid(), name);
    }

    public class SayHelloHandler : IHandleMessage<SayHello>
    {
        private readonly ILogger<SayHelloHandler> _logger;

        public SayHelloHandler(ILogger<SayHelloHandler> logger)
        {
            _logger = logger;
        }

        public Task HandleAsync(IMessageContext<SayHello> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"hi {context.Message.Name}!");

            return Task.CompletedTask;
        }
    }
}
