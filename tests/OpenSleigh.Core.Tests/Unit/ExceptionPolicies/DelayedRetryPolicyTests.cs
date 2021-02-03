using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class DelayedRetryPolicyTests
    {
        [Fact]
        public void ctor_should_throw_when_arguments_null()
        {
            var filters = new ExceptionFilters(Enumerable.Empty<ExceptionFilter>());
            DelayFactory delayFactory = i => TimeSpan.Zero;

            Assert.Throws<ArgumentNullException>(() => new DelayedRetryPolicy(1, null, delayFactory));
            Assert.Throws<ArgumentNullException>(() => new DelayedRetryPolicy(1, filters, null));
        }

        [Fact]
        public async Task WrapAsync_should_run_delay_factory_on_exception()
        {
            var filters = new ExceptionFilters(Enumerable.Empty<ExceptionFilter>());

            var hit = false;
            DelayFactory delayFactory = i =>
            {
                hit = true;
                return TimeSpan.Zero;
            };

            var sut = new DelayedRetryPolicy(1, filters, delayFactory);

            await Assert.ThrowsAsync<ApplicationException>(async () => await sut.WrapAsync(() =>
            {
                throw new ApplicationException();
                return Task.FromResult(true);
            }));

            hit.Should().BeTrue();
        }

        [Fact]
        public async Task WrapAsync_should_run_OnException_handler()
        {
            var filters = new ExceptionFilters(Enumerable.Empty<ExceptionFilter>());

            DelayFactory delayFactory = i => TimeSpan.Zero;

            var hit = false;
            OnExceptionHandler onException = ctx =>
            {
                hit = true;
                return Task.CompletedTask;
            };

            var sut = new DelayedRetryPolicy(1, filters, delayFactory, onException);

            await Assert.ThrowsAsync<ApplicationException>(async () => await sut.WrapAsync(() =>
            {
                throw new ApplicationException();
                return Task.FromResult(true);
            }));

            hit.Should().BeTrue();
        }
    }
}