using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Messaging;
using OpenSleigh.Core.Tests.Sagas;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class SagasRunnerTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null(){
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var sp = NSubstitute.Substitute.For<IServiceScopeFactory>();

            Assert.Throws<ArgumentNullException>(() => new SagasRunner(null, stateTypeResolver, typesCache));
            Assert.Throws<ArgumentNullException>(() => new SagasRunner(sp, null, typesCache));
            Assert.Throws<ArgumentNullException>(() => new SagasRunner(sp, stateTypeResolver, null));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_message_null()
        {
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var sp = NSubstitute.Substitute.For<IServiceScopeFactory>();

            var sut = new SagasRunner(sp, stateTypeResolver, typesCache);

            await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunAsync<StartDummySaga>(null));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_runner_fails()
        {
            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var types = (typeof(DummySaga), typeof(DummySagaState));
            stateTypeResolver.Resolve<StartDummySaga>()
                .Returns(new[]{types});

            var expectedException = new Exception("whoops");
            var runner = NSubstitute.Substitute.For<ISagaRunner<DummySaga, DummySagaState>>();
            runner.When(r => r.RunAsync(messageContext, Arg.Any<CancellationToken>()))
                .Throw(expectedException);
                
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(ISagaRunner<DummySaga, DummySagaState>))
                .Returns(runner);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);
            
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            typesCache.GetGeneric(typeof(ISagaRunner<,>), typeof(DummySaga), typeof(DummySagaState))
                        .Returns(typeof(ISagaRunner<DummySaga, DummySagaState>));

            var sut = new SagasRunner(scopeFactory, stateTypeResolver, typesCache);

            var ex = await Assert.ThrowsAsync<AggregateException>(() => sut.RunAsync(messageContext));
            ex.Message.Should().Contain("an error has occurred");
            ex.InnerExceptions.Should().NotBeNullOrEmpty()
                .And.HaveCount(1)
                .And.Contain(e => e.Message == expectedException.Message);
        }

        [Fact]
        public async Task RunAsync_should_execute_saga_runner()
        {
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var types = (typeof(DummySaga), typeof(DummySagaState));
            stateTypeResolver.Resolve<StartDummySaga>()
                .Returns(new[] { types });

            var runner = NSubstitute.Substitute.For<ISagaRunner<DummySaga, DummySagaState>>();

            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(ISagaRunner<DummySaga, DummySagaState>))
                .Returns(runner);

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);

            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            typesCache.GetGeneric(typeof(ISagaRunner<,>), typeof(DummySaga), typeof(DummySagaState))
                        .Returns(typeof(ISagaRunner<DummySaga, DummySagaState>));

            var sut = new SagasRunner(scopeFactory, stateTypeResolver, typesCache);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            await sut.RunAsync(messageContext);

            await runner.Received(1)
                .RunAsync(messageContext, Arg.Any<CancellationToken>());
        }
    }
}