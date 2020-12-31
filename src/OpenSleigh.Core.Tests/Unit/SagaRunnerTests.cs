using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using NSubstitute;
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Core.Tests
{
    public class SagaRunnerTests
    {
        [Fact]
        public async Task RunAsync_should_retry_if_saga_state_locked()
        {
            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);

            var firstCall = true;
            sagaStateService.When(s => s.GetAsync(messageContext, Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    throw new LockException("lorem");
                }
            });
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService);

            await sut.RunAsync(messageContext, CancellationToken.None);

            await saga.Received(1)
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());
            await sagaStateService.Received(2)
                .GetAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_throw_MessageException_if_message_already_processed()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();
            
            var state = new DummySagaState(message.CorrelationId);
            state.SetAsProcessed(message);

            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService);

            await Assert.ThrowsAsync<MessageException>(() => sut.RunAsync(messageContext, CancellationToken.None));
        }

        [Fact]
        public async Task RunAsync_should_throw_SagaNotFoundException_if_saga_cannot_be_build()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService);

            await Assert.ThrowsAsync<SagaNotFoundException>(() => sut.RunAsync(messageContext, CancellationToken.None));
        }

        [Fact]
        public async Task RunAsync_should_execute_all_registered_sagas()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService);

            await sut.RunAsync(messageContext, CancellationToken.None);

            await saga.Received(1)
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());

        }
    }
}