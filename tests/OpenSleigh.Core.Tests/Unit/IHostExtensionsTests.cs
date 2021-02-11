using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class IHostExtensionsTests
    {
        [Fact]
        public async Task SetupInfrastructureAsync_should_run_configured_installers()
        {
            var installers = new[]
            {
                NSubstitute.Substitute.For<IInfrastructureCreator>(),
                NSubstitute.Substitute.For<IInfrastructureCreator>(),
                NSubstitute.Substitute.For<IInfrastructureCreator>()
            };
            var sp = NSubstitute.Substitute.For<IServiceProvider>();

            var scope = NSubstitute.Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            
            var scopeFactory = NSubstitute.Substitute.For<IServiceScopeFactory>();
            scopeFactory.CreateScope().Returns(scope);
            sp.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);
            
            sp.GetService(typeof(IEnumerable<IInfrastructureCreator>))
                .Returns(installers);
            
            var sut = NSubstitute.Substitute.For<IHost>();
            sut.Services.Returns(sp);

            await sut.SetupInfrastructureAsync();

            foreach (var installer in installers)
                await installer.Received(1)
                    .SetupAsync(sut);
        }
    }
}