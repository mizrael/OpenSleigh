using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaStateServiceTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();
            
            Assert.Throws<ArgumentNullException>(() =>
                new SagaStateService<DummySaga, DummySagaState>(null, sagaStateRepo, outboxRepository));

            Assert.Throws<ArgumentNullException>(() =>
                new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, null, outboxRepository));

            Assert.Throws<ArgumentNullException>(() =>
                new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, null));
        }
        
        [Fact]
        public async Task GetAsync_should_throw_StateCreationException_if_saga_state_cannot_be_build()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();

            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, outboxRepository);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<StateCreationException>(() =>
                             sut.GetAsync(messageContext, CancellationToken.None));
            ex.Message.Should().Contain("unable to create State instance with type");
        }

        [Fact]
        public async Task GetAsync_should_throw_MessageException_if_message_cannot_start_saga()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();

            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, outboxRepository);

            var message = DummySagaStarted.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<DummySagaStarted>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<MessageException>(() =>
                         sut.GetAsync(messageContext, CancellationToken.None));
            ex.Message.Should().Contain($"Saga '{message.CorrelationId}' cannot be started by message");

            sagaStateFactory.DidNotReceiveWithAnyArgs()
                .Create(null);
        }

        [Fact]
        public async Task GetAsync_should_return_state_from_factory_if_message_can_start_saga()
        {
            var expectedState = new DummySagaState(Guid.NewGuid());

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            sagaStateFactory.Create(message)
                .Returns(expectedState);

            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            sagaStateRepo.LockAsync(message.CorrelationId, expectedState, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult((expectedState, Guid.NewGuid())));

            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();

            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, outboxRepository);

            var result = await sut.GetAsync(messageContext, CancellationToken.None);
            result.state.Should().Be(expectedState);
        }

        [Fact]
        public async Task SaveAsync_should_unlock_state()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();

            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            
            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();

            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, outboxRepository);

            var state = new DummySagaState(Guid.NewGuid());
            var lockId = Guid.NewGuid();
            var saga = new DummySaga(state);

            await sut.SaveAsync(saga, lockId, CancellationToken.None);

            await sagaStateRepo.Received(1)
                .ReleaseLockAsync(state, lockId, CancellationToken.None);
        }

        [Fact]
        public async Task SaveAsync_should_persist_outbox()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();

            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();

            var outboxRepository = NSubstitute.Substitute.For<IOutboxRepository>();

            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, sagaStateRepo, outboxRepository);

            var state = new DummySagaState(Guid.NewGuid());
            var saga = new DummySaga(state);

            var message = StartDummySaga.New();
            saga.PublishTestWrapper(message);
            var lockId = Guid.NewGuid();

            await sut.SaveAsync(saga, lockId, CancellationToken.None);

            await outboxRepository.Received(1)
                     .AppendAsync(
                        Arg.Is<IEnumerable<IMessage>>(msg => msg.Contains(message)),
                        Arg.Any<CancellationToken>());
        }
    }
}