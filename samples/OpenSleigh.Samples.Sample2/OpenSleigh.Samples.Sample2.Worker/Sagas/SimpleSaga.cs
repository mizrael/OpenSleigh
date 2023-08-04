using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenSleigh.Samples.Sample2.Common.Messages;
using OpenSleigh.Transport;
using OpenSleigh.Utils;

namespace OpenSleigh.Samples.Sample2.Worker.Sagas
{
    public record SimpleSagaState;

    public record ProcessSimpleSaga(Guid Id, Guid CorrelationId) : IMessage { }

    public record SimpleSagaCompleted(Guid Id, Guid CorrelationId) : IMessage { }

    public class SimpleSaga :
        Saga<SimpleSagaState>,
        IStartedBy<StartSimpleSaga>,
        IHandleMessage<ProcessSimpleSaga>,
        IHandleMessage<SimpleSagaCompleted>
    {
        private readonly ILogger<SimpleSaga> _logger;

        public SimpleSaga(
            ILogger<SimpleSaga> logger,
            ISagaExecutionContext<SimpleSagaState> context,
            ISerializer serializer) : base(context, serializer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async ValueTask HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"starting saga '{context.Message.CorrelationId}'...");
            
            var message = new ProcessSimpleSaga(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }
        
        public async ValueTask HandleAsync(IMessageContext<ProcessSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"processing saga '{context.Message.CorrelationId}'...");
            
            var message = new SimpleSagaCompleted(Guid.NewGuid(), context.Message.CorrelationId);
            this.Publish(message);
        }

        public ValueTask HandleAsync(IMessageContext<SimpleSagaCompleted> context, CancellationToken cancellationToken = default)
        {
            this.Context.MarkAsCompleted();
            _logger.LogInformation($"saga '{context.Message.CorrelationId}' completed!");
            return ValueTask.CompletedTask;
        }
    }
}
