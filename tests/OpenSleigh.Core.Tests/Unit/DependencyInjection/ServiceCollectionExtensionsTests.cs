using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenSleigh.Core.DependencyInjection;
using OpenSleigh.Core.Messaging;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.DependencyInjection
{
    public class ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddBusSubscriber_should_register_subscriber()
        {
            var sut = new ServiceCollection();
            sut.AddBusSubscriber(typeof(FakeSubscriber));

            sut.Any(sd => sd.Lifetime == ServiceLifetime.Singleton &&
                          sd.ServiceType == typeof(ISubscriber) &&
                          sd.ImplementationType == typeof(FakeSubscriber)).Should().BeTrue();
        }
        
        [Fact]
        public void AddBusSubscriber_should_not_register_subscriber_if_already_added()
        {
            var sut = new ServiceCollection();
            sut.AddBusSubscriber(typeof(FakeSubscriber));
            sut.AddBusSubscriber(typeof(FakeSubscriber));
            sut.AddBusSubscriber(typeof(FakeSubscriber));
            
            sut.Count(sd => sd.Lifetime == ServiceLifetime.Singleton &&
                          sd.ServiceType == typeof(ISubscriber) &&
                          sd.ImplementationType == typeof(FakeSubscriber))
                .Should().Be(1);
        }
    }
    
    internal class FakeSubscriber : ISubscriber
    {
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}