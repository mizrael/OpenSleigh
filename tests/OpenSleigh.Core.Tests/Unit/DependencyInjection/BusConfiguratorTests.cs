﻿using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using Xunit;
using NSubstitute;
using OpenSleigh.Core.Tests.Sagas;

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
            var sysInfo = SystemInfo.New();
            
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
    }
}
