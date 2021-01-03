using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Persistence;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaStateServiceTests
    {
        [Fact]
        public async Task GetAsync_should_throw_StateCreationException_if_saga_state_cannot_be_build()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var uow = NSubstitute.Substitute.For<IUnitOfWork>();
            uow.SagaStatesRepository.Returns(sagaStateRepo);
            
            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, uow);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<StateCreationException>(() =>
                             sut.GetAsync(messageContext, CancellationToken.None));
            ex.Message.Should().Contain("unable to create saga state instance");
        }

        [Fact]
        public async Task GetAsync_should_throw_StateCreationException_if_message_cannot_start_saga()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();
            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var uow = NSubstitute.Substitute.For<IUnitOfWork>();
            uow.SagaStatesRepository.Returns(sagaStateRepo);
            
            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, uow);

            var message = DummySagaStarted.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<DummySagaStarted>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<StateCreationException>(() =>
                         sut.GetAsync(messageContext, CancellationToken.None));
            ex.Message.Should().Contain("saga cannot be started by message");
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

            var uow = NSubstitute.Substitute.For<IUnitOfWork>();
            uow.SagaStatesRepository.Returns(sagaStateRepo);
            
            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, uow);

            var result = await sut.GetAsync(messageContext, CancellationToken.None);
            result.state.Should().Be(expectedState);
        }

        [Fact]
        public async Task SaveAsync_should_unlock_state()
        {
            var sagaStateFactory = NSubstitute.Substitute.For<ISagaStateFactory<DummySagaState>>();

            var sagaStateRepo = NSubstitute.Substitute.For<ISagaStateRepository>();
            var uow = NSubstitute.Substitute.For<IUnitOfWork>();
            uow.SagaStatesRepository.Returns(sagaStateRepo);
            
            var sut = new SagaStateService<DummySaga, DummySagaState>(sagaStateFactory, uow);

            var state = new DummySagaState(Guid.NewGuid());
            var lockId = Guid.NewGuid();

            await sut.SaveAsync(state, lockId, CancellationToken.None);

            await sagaStateRepo.Received(1)
                .UpdateAsync(state, lockId, true, CancellationToken.None);
        }
    }
}