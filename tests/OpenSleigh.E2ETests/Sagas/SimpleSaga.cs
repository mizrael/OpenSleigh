using OpenSleigh.Messaging;
using OpenSleigh.Utils;

namespace OpenSleigh.E2ETests
{
    public record StartSimpleSaga : IMessage
    {
        public int Foo { get; init; }
        public string Bar { get; init; }        
    }

    public class SimpleSaga : 
        Saga, 
        IStartedBy<StartSimpleSaga>
    {
        private readonly Action<IMessageContext<StartSimpleSaga>> _onStart;

        public SimpleSaga(
            Action<IMessageContext<StartSimpleSaga>> onStart, 
            ISagaExecutionContext context,
            ISerializer serializer) :  base(context, serializer)
        {
            _onStart = onStart;
        }

        public ValueTask HandleAsync(IMessageContext<StartSimpleSaga> context, CancellationToken cancellationToken = default)
        {
            _onStart?.Invoke(context);
            return ValueTask.CompletedTask;
        }
    }
}