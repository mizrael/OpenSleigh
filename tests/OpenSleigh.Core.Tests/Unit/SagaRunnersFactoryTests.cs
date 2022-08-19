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
    public class SagaRunnersFactoryTests
    {
        [Fact]
        public void ctor_should_throw_if_arguments_null()
        {
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();

            Assert.Throws<ArgumentNullException>(() => new SagaRunnersFactory(null, typesCache));
            Assert.Throws<ArgumentNullException>(() => new SagaRunnersFactory(stateTypeResolver, null));
        }

        [Fact]
        public void Create_should_return_empty_collection_when_no_runners_registered()
        {
            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            var stateTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var sp = NSubstitute.Substitute.For<IServiceProvider>();
            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);

            var sut = new SagaRunnersFactory(stateTypeResolver, typesCache);
            var results = sut.Create<DummyMessage>(scope);
            results.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void Create_should_return_registered_runners()
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

            var typesCache = NSubstitute.Substitute.For<ITypesCache>();
            typesCache.GetGeneric(typeof(ISagaRunner<,>), typeof(DummySaga), typeof(DummySagaState))
                        .Returns(typeof(ISagaRunner<DummySaga, DummySagaState>));

            var sut = new SagaRunnersFactory(stateTypeResolver, typesCache);
            var results = sut.Create<StartDummySaga>(scope);
            results.Should().NotBeNullOrEmpty();
        }
    }
}