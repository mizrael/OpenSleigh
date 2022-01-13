using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenSleigh.Core;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Samples.Sample1.Console.Sagas;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace OpenSleigh.Samples.Sample1.Console.Tests
{
    public class MySagaTests
    {
        [Fact]
        public async Task HandleAsync_StartSaga_should_publish_ProcessMySaga_message()
        {
            var state = new MySagaState(Guid.NewGuid());
            var logger = NSubstitute.Substitute.For<ILogger<MySaga>>();
            var sut = new MySaga(logger, state);

            var message = new StartSaga(Guid.NewGuid(), Guid.NewGuid());
            var ctx = new FakeMessageContext<StartSaga>(message);
            await sut.HandleAsync(ctx);

            state.Outbox.Count.Should().Be(1);
            var outgoingMessage = state.Outbox.First();
            outgoingMessage.Should().BeOfType<ProcessMySaga>();
            outgoingMessage.CorrelationId.Should().Be(message.CorrelationId);  
        }
    }

    public class FakeMessageContext<TM> : IMessageContext<TM> 
        where TM : IMessage
    {
        public FakeMessageContext(TM message)
        {
            Message = message;
        }

        public TM Message { get; }

        public ISystemInfo SystemInfo => throw new NotImplementedException();
    }
}
