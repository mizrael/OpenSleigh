using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using FluentAssertions;
using OpenSleigh.Core.DependencyInjection;
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
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            Assert.Throws<ArgumentNullException>(() => new SagasRunner(null, stateTypeResolver, typesCache));
            Assert.Throws<ArgumentNullException>(() => new SagasRunner(sp, null, typesCache));
            Assert.Throws<ArgumentNullException>(() => new SagasRunner(sp, stateTypeResolver, null));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_message_null()
        {
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var sut = new SagasRunner(sp, stateTypeResolver, typesCache);

            await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RunAsync<StartDummySaga>(null));
        }

        [Fact]
        public async Task RunAsync_should_throw_if_no_saga_registered()
        {
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var sut = new SagasRunner(sp, stateTypeResolver, typesCache);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<SagaNotFoundException>(() => sut.RunAsync(messageContext));
            ex.Message.Should().Contain("no saga registered for message of type");
        }

        [Fact]
        public async Task RunAsync_should_throw_SagaNotFoundException_if_no_saga_registered_on_DI()
        {
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var types = (typeof(DummySaga), typeof(DummySagaState));
            stateTypeResolver.Resolve<StartDummySaga>()
                .Returns(types);

            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            typesCache.GetGeneric(typeof(ISagaRunner<,>), typeof(DummySaga), typeof(DummySagaState))
                        .Returns(typeof(ISagaRunner<DummySaga, DummySagaState>));

            var sut = new SagasRunner(sp, stateTypeResolver, typesCache);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            var ex = await Assert.ThrowsAsync<SagaNotFoundException>(() => sut.RunAsync(messageContext));
            ex.Message.Should().Contain("no saga registered on DI for message of type");
        }

        [Fact]
        public async Task RunAsync_should_execute_saga_runner()
        {
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var types = (typeof(DummySaga), typeof(DummySagaState));
            stateTypeResolver.Resolve<StartDummySaga>()
                .Returns(types);

            var runner = NSubstitute.Substitute.For<ISagaRunner<DummySaga, DummySagaState>>();

            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            sp.GetService(typeof(ISagaRunner<DummySaga, DummySagaState>))
                .Returns(runner);

            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            typesCache.GetGeneric(typeof(ISagaRunner<,>), typeof(DummySaga), typeof(DummySagaState))
                        .Returns(typeof(ISagaRunner<DummySaga, DummySagaState>));

            var sut = new SagasRunner(sp, stateTypeResolver, typesCache);

            var message = StartDummySaga.New();
            var messageContext = NSubstitute.Substitute.For<IMessageContext<StartDummySaga>>();
            messageContext.Message.Returns(message);

            await sut.RunAsync(messageContext);

            await runner.Received(1)
                .RunAsync(messageContext, Arg.Any<CancellationToken>());
        }
    }
}