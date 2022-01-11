using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using Xunit;
using NSubstitute;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using System;

namespace OpenSleigh.Core.Tests.Unit.DependencyInjection
{
    public class BusConfiguratorTests
    {
        [Fact]
        public void AddMessageHandlers_should_add_all_available_message_handlers_from_assemblies()
        {
            var services = NSubstitute.Substitute.For<IServiceCollection>();
            var sagaTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");
            
            var sut = new BusConfigurator(services, sagaTypeResolver, typeResolver, sysInfo);
            var result = sut.AddMessageHandlers<DummyMessage>(new[] { typeof(BusConfiguratorTests).Assembly });

            result.Should().NotBeNull();
            
            typeResolver.Received(1)
                .Register(typeof(DummyMessage));
            
            services.Received(1).Add(Arg.Any<ServiceDescriptor>());

            services.Received(1)
               .Add(Arg.Is<ServiceDescriptor>(sd => sd.Lifetime == ServiceLifetime.Transient &&
                                                 sd.ServiceType == typeof(IHandleMessage<DummyMessage>) && 
                                                 sd.ImplementationType.IsAssignableTo(typeof(DummyMessageHandler))));
        }

        [Fact] 
        public void WithTransportSerializer_should_replace_existing_registration()
        {            
            var services = new ServiceCollection();
            var sagaTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");

            var sut = new BusConfigurator(services, sagaTypeResolver, typeResolver, sysInfo);

            var newSerializer = new FakeTransportSerializer();
            sut.WithTransportSerializer(newSerializer);
            
            var sp = services.BuildServiceProvider();
            var serializer = sp.GetService<ITransportSerializer>();
            serializer.Should().Be(newSerializer);
        }

        [Fact]
        public void WithTransportSerializer_should_replace_existing_registration_when_input_null()
        {
            var services = new ServiceCollection();
            var sagaTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");

            var sut = new BusConfigurator(services, sagaTypeResolver, typeResolver, sysInfo);

            sut.WithTransportSerializer<FakeTransportSerializer>();

            var sp = services.BuildServiceProvider();
            var serializer = sp.GetService<ITransportSerializer>();
            serializer.Should().NotBeNull()
                .And.BeOfType<FakeTransportSerializer>();
        }

        [Fact]
        public void WithPersistenceSerializer_should_replace_existing_registration()
        {
            var services = new ServiceCollection();
            var sagaTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");

            var sut = new BusConfigurator(services, sagaTypeResolver, typeResolver, sysInfo);

            var newSerializer = new FakePersistenceSerializer();
            sut.WithPersistenceSerializer(newSerializer);

            var sp = services.BuildServiceProvider();
            var serializer = sp.GetService<IPersistenceSerializer>();
            serializer.Should().Be(newSerializer);
        }

        [Fact]
        public void WithPersistenceSerializer_should_replace_existing_registration_when_input_null()
        {
            var services = new ServiceCollection();
            var sagaTypeResolver = NSubstitute.Substitute.For<ISagaTypeResolver>();
            var typeResolver = NSubstitute.Substitute.For<ITypeResolver>();
            var sysInfo = new SystemInfo(Guid.NewGuid(), "test");

            var sut = new BusConfigurator(services, sagaTypeResolver, typeResolver, sysInfo);

            sut.WithPersistenceSerializer<FakePersistenceSerializer>();

            var sp = services.BuildServiceProvider();
            var serializer = sp.GetService<IPersistenceSerializer>();
            serializer.Should().NotBeNull()
                .And.BeOfType<FakePersistenceSerializer>();
        }
    }
}
