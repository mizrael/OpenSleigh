using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class TypesCacheTests
    {
        [Fact]
        public void GetGeneric_should_return_typed_generic_from_raw_type()
        {
            var baseType = typeof(IEnumerable<>);
            var expectedType = typeof(IEnumerable<int>);

            var sut = new TypesCache();
            var result = sut.GetGeneric(baseType, typeof(int));
            result.Should().Be(expectedType);
        }

        [Fact]
        public void GetMethod_should_return_null_when_method_not_found()
        {
            var sut = new TypesCache();
            var result = sut.GetMethod(typeof(TypesCacheTests), "foo");
            result.Should().BeNull();
        }

        [Fact]
        public void GetMethod_should_return_method_when_name_valid()
        {
            var type = typeof(TypesCacheTests);
            var methodName = nameof(TypesCacheTests.GetMethod_should_return_method_when_name_valid);
            var expectedMethod = type.GetMethod(methodName);
            
            var sut = new TypesCache();
            var result = sut.GetMethod(type, methodName);
            result.Should().BeSameAs(expectedMethod);
        }
    }
}
