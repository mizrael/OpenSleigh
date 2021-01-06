using System;
using FluentAssertions;
using Xunit;

namespace OpenSleigh.Core.Tests.Unit
{
    public class TypeResolverTests
    {
        [Fact]
        public void Register_should_throw_if_input_null()
        {
            var sut = new TypeResolver();
            Assert.Throws<ArgumentNullException>(() => sut.Register((Type)null));
        }

        [Fact]
        public void Resolve_should_resolve_by_fullname()
        {
            var sut = new TypeResolver();
            sut.Register(typeof(TypeResolverTests));

            var result = sut.Resolve(typeof(TypeResolverTests).FullName);
            result.Should().Be(typeof(TypeResolverTests));
        }

        [Fact]
        public void Resolve_should_resolve_by_name()
        {
            var sut = new TypeResolver();
            sut.Register(typeof(TypeResolverTests));

            var result = sut.Resolve(typeof(TypeResolverTests).Name);
            result.Should().Be(typeof(TypeResolverTests));
        }
    }
}
