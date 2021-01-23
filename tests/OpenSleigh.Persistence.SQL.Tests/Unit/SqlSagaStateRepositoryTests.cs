using System;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Unit
{
    public class SqlSagaStateRepositoryTests
    {
        
        [Fact]
        public void ctor_should_throw_when_input_null()
        {
            Assert.Throws<ArgumentNullException>(() => new SqlSagaStateRepository(null));
        }
    }
}
