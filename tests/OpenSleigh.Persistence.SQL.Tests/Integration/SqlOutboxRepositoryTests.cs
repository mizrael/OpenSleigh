using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenSleigh.Core.Exceptions;
using OpenSleigh.Core.Tests.Sagas;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Integration
{
    public class SqlOutboxRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SqlOutboxRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task LockAsync_should_throw_if_message_not_found()
        {
            var message = StartDummySaga.New();
            var sut = CreateSut();
            await Assert.ThrowsAsync<ArgumentException>(async () => await sut.LockAsync(message));
        }

        private SqlOutboxRepository CreateSut()
        {
            var sut = new SqlOutboxRepository(_fixture.DbContext, new JsonSerializer());
            return sut;
        }
    }
}
