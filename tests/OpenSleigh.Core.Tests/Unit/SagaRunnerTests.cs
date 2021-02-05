using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenSleigh.Core.ExceptionPolicies;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Persistence;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagaRunnerTests
    {
        [Fact]
        public void ctor_should_throw_when_arguments_null(){
            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();
            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            Assert.Throws<ArgumentNullException>(() => new SagaRunner<DummySaga, DummySagaState>(null, sagaStateService, transactionManager, policyFactory, logger));
            Assert.Throws<ArgumentNullException>(() => new SagaRunner<DummySaga, DummySagaState>(sagaFactory, null, transactionManager, policyFactory, logger));
            Assert.Throws<ArgumentNullException>(() => new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, null, policyFactory, logger));
            Assert.Throws<ArgumentNullException>(() => new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, null, logger));
            Assert.Throws<ArgumentNullException>(() => new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, null));
        }

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
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext, CancellationToken.None);
            
            await sagaStateService.Received(2)
                .GetAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_not_execute_handler_if_message_already_processed()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var state = new DummySagaState(message.CorrelationId);
            state.SetAsProcessed(message);

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();
           
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            
            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext, CancellationToken.None);

            sagaFactory.DidNotReceiveWithAnyArgs().Create(null);

            await saga.DidNotReceiveWithAnyArgs()
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_not_execute_handler_if_saga_is_marked_as_completed()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var state = new DummySagaState(message.CorrelationId);
            state.MarkAsCompleted();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            
            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext, CancellationToken.None);

            sagaFactory.DidNotReceiveWithAnyArgs().Create(null);

            await saga.DidNotReceiveWithAnyArgs()
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_throw_if_saga_cannot_be_build()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            
            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await Assert.ThrowsAsync<SagaException>(() => sut.RunAsync(messageContext, CancellationToken.None));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_saga_cannot_handle_message()
        {
            var message = UnhandledMessage.New();

            var messageContext = NSubstitute.Substitute.For<IMessageContext<UnhandledMessage>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await Assert.ThrowsAsync<ConsumerNotFoundException>(() => sut.RunAsync(messageContext, CancellationToken.None));
        }

        [Fact]
        public async Task RunAsync_should_execute_saga_handler_without_policy_when_not_available()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            policyFactory.Create<StartDummySaga>().ReturnsNullForAnyArgs();
            
            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext, CancellationToken.None);

            await saga.Received(1)
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_execute_saga_handler_with_policy_when_available()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            var policy = new FakePolicy();
            policyFactory.Create<StartDummySaga>().ReturnsForAnyArgs(policy);

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext, CancellationToken.None);

            await saga.Received(1)
                .HandleAsync(messageContext, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task RunAsync_should_use_transaction()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .DoNotCallBase();

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transaction = NSubstitute.Substitute.For<ITransaction>();
            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            transactionManager.StartTransactionAsync(default)
                .ReturnsForAnyArgs(transaction);
            
            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            policyFactory.Create<StartDummySaga>().ReturnsNullForAnyArgs();

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);

            await sut.RunAsync(messageContext);

            await transaction.Received(1)
                .CommitAsync(default);
        }

        [Fact]
        public async Task RunAsync_should_rollback_transaction_if_exception_occurs()
        {
            var message = new StartDummySaga(Guid.NewGuid(), Guid.NewGuid());

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sagaStateService = NSubstitute.Substitute.For<ISagaStateService<DummySaga, DummySagaState>>();

            var state = new DummySagaState(message.CorrelationId);
            sagaStateService.GetAsync(messageContext, Arg.Any<CancellationToken>())
                .Returns((state, Guid.NewGuid()));

            var saga = NSubstitute.Substitute.ForPartsOf<DummySaga>();
            saga.SetBus(NSubstitute.Substitute.For<IMessageBus>());
          
            var expectedException = new ApplicationException("whoops");
            saga.When(s => s.HandleAsync(Arg.Any<IMessageContext<StartDummySaga>>(), Arg.Any<CancellationToken>()))
                .Throw(expectedException);

            var sagaFactory = NSubstitute.Substitute.For<ISagaFactory<DummySaga, DummySagaState>>();
            sagaFactory.Create(state)
                .Returns(saga);

            var logger = NSubstitute.Substitute.For<ILogger<SagaRunner<DummySaga, DummySagaState>>>();

            var transaction = NSubstitute.Substitute.For<ITransaction>();
            var transactionManager = NSubstitute.Substitute.For<ITransactionManager>();
            transactionManager.StartTransactionAsync(default)
                .ReturnsForAnyArgs(transaction);

            var policyFactory = NSubstitute.Substitute.For<ISagaPolicyFactory<DummySaga>>();
            policyFactory.Create<StartDummySaga>().ReturnsNullForAnyArgs();

            var sut = new SagaRunner<DummySaga, DummySagaState>(sagaFactory, sagaStateService, transactionManager, policyFactory, logger);
            
            var ex = await Assert.ThrowsAsync<ApplicationException>(async() => await sut.RunAsync(messageContext));
            ex.Should().Be(expectedException);
            
            await transaction.Received(1)
                .RollbackAsync(default);
        }
    }

    internal class FakePolicy : IPolicy
    {
        public Task<TRes> WrapAsync<TRes>(Func<Task<TRes>> action)
        {
            return action();
        }
    }
}