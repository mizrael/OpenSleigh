using System;
using OpenSleigh.Core.Utils;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlSagaStateRepositoryTests
    {
        
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            var serializer = NSubstitute.Substitute.For<ISerializer>();
            var dbContext = NSubstitute.Substitute.For<ISagaDbContext>();
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null, serializer));
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(dbContext, null));
        }
    }
}
