using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.ExceptionPolicies;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit.ExceptionPolicies
{
    public class RetryPolicyTests
    {
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new RetryPolicy(1, null));
        }

        [Fact]
        public async Task WrapAsync_should_not_execute_action_when_max_retries_below_1()
        {
            var filters = new ExceptionFilters(Enumerable.Empty<ExceptionFilter>());
            var sut = new RetryPolicy(0, filters);

            var hit = false;
            Func<Task<bool>> action = () =>
            {
                hit = true;
                return Task.FromResult(hit);
            };
            var result = await sut.WrapAsync(action);

            result.Should().BeFalse();
            hit.Should().BeFalse();
        }

        [Fact]
        public async Task WrapAsync_should_throw_last_exception_when_retries_exhausted()
        {
            int maxRetries = 3;
            var filters = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ApplicationException)
            });
            var sut = new RetryPolicy(maxRetries, filters);

            int count = 0;
            Func<Task<bool>> action = () =>
            {
                count++;
                throw new ApplicationException(count.ToString());
            };
            var ex = await Assert.ThrowsAsync<ApplicationException>(async () => await sut.WrapAsync(action));
            ex.Message.Should().Be(count.ToString());

            count.Should().Be(maxRetries);
        }

        [Fact]
        public async Task WrapAsync_should_throw_exception_immediately_if_not_handled()
        {
            int maxRetries = 3;
            var filters = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ArgumentNullException)
            });
            var sut = new RetryPolicy(maxRetries, filters);

            int count = 0;
            Func<Task<bool>> action = () =>
            {
                count++; 
                throw new ApplicationException();
            };
            await Assert.ThrowsAsync<ApplicationException>(async () => await sut.WrapAsync(action));

            count.Should().Be(1);
        }

        [Fact]
        public async Task WrapAsync_should_call_on_exception_handler()
        {
            int maxRetries = 1;
            var filters = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ApplicationException)
            });
            
            var hit = false;
            OnExceptionHandler onException = ctx =>
            {
                hit = true;
                return Task.CompletedTask;
            };
            
            var sut = new RetryPolicy(maxRetries, filters, onException);
            
            Func<Task<bool>> action = () => throw new ApplicationException();
            await Assert.ThrowsAsync<ApplicationException>(async () => await sut.WrapAsync(action));

            hit.Should().BeTrue();
        }

        [Fact]
        public async Task WrapAsync_should_return_action_result()
        {
            int maxRetries = 3;
            var filters = new ExceptionFilters(new[]
            {
                new ExceptionFilter(ex => ex is ArgumentNullException)
            });
            var sut = new RetryPolicy(maxRetries, filters);
            
            Func<Task<bool>> action = () => Task.FromResult(true);
            var res = await sut.WrapAsync(action);

            res.Should().BeTrue();
        }
    }
}