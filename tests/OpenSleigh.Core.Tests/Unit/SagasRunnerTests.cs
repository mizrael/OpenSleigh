using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using FluentAssertions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagasRunnerTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            var runnersFactory = NSubstitute.Substitute.For<ISagaRunnersFactory>();

            Assert.Throws<ArgumentNullException>(() => new SagasRunner(null, scopeFactory));
            Assert.Throws<ArgumentNullException>(() => new SagasRunner(runnersFactory, null));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_message_null()
        {
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            var runnersFactory = NSubstitute.Substitute.For<ISagaRunnersFactory>();

            var sut = new SagasRunner(runnersFactory, scopeFactory);

            await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunAsync<StartDummySaga>(null));
        }

        [Fact]
        public async Task RunAsync_should_do_nothing_when_no_runners_available()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateAsyncScope().Returns(scope);

            var runnersFactory = NSubstitute.Substitute.For<ISagaRunnersFactory>();

            var sut = new SagasRunner(runnersFactory, scopeFactory);

            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            var result = sut.RunAsync(messageContext);
            result.Should().Be(Task.CompletedTask);
        }

        [Fact]
        public async Task RunAsync_should_throw_if_runner_fails()
        {
            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateAsyncScope().Returns(scope);

            var expectedException = new Exception("whoops");
            var runner = NSubstitute.Substitute.For<ISagaRunner<DummySaga, DummySagaState>>();
            runner.When(r => r.RunAsync(messageContext, Arg.Any<CancellationToken>()))
                .Throw(expectedException);

            var runnersFactory = NSubstitute.Substitute.For<ISagaRunnersFactory>();
            runnersFactory.Create<StartDummySaga>(scope)
                .ReturnsForAnyArgs(new[] { runner });

            var sut = new SagasRunner(runnersFactory, scopeFactory);

            var ex = await Assert.ThrowsAsync<AggregateException>(() => sut.RunAsync(messageContext));
            ex.InnerExceptions.Should().NotBeNullOrEmpty()
                .And.HaveCount(1)
                .And.Contain(e => e.Message == expectedException.Message);
        }

        [Fact]
        public async Task RunAsync_should_execute_saga_runner_registered_for_message()
        {
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateAsyncScope().Returns(scope);

            var runners = Enumerable.Range(1, 5)
                .Select(i => Substitute.For<ISagaRunner<DummySaga, DummySagaState>>())
                .ToArray();

            var runnersFactory = NSubstitute.Substitute.For<ISagaRunnersFactory>();
            runnersFactory.Create<StartDummySaga>(scope)
                            .ReturnsForAnyArgs(runners);

            var sut = new SagasRunner(runnersFactory, scopeFactory);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            await sut.RunAsync(messageContext);

            foreach (var runner in runners)
                await runner.Received(1)
                            .RunAsync(messageContext, Arg.Any<CancellationToken>());
        }
    }
}