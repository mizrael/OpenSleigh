using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using OpenSleigh.Core.Utils;
using OpenSleigh.Persistence.SQL.Tests.Fixtures;
using Xunit;

namespace OpenSleigh.Persistence.SQL.Tests.Integration
{
    [Category("Integration")]
    [Trait("Category", "Integration")]
    public class SqlSagaStateRepositoryTests : IClassFixture<DbFixture>
    {
        private readonly DbFixture _fixture;

        public SqlSagaStateRepositoryTests(DbFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task LockAsync_should_create_and_return_locked_item_if_not_existing()
        {
            var sut = CreateSut();

            var newState = DummyState.New();

            var (state, lockId) = await sut.LockAsync(newState.Id, newState, CancellationToken.None);
            state.Should().NotBeNull();
            state.Id.Should().Be(newState.Id);
            state.Bar.Should().Be(newState.Bar);
            state.Foo.Should().Be(newState.Foo);
        }

        private SqlSagaStateRepository CreateSut()
        {
            var serializer = new JsonSerializer();
            var sut = new SqlSagaStateRepository(_fixture.DbContext, serializer);
            return sut;
        }
    }
}
