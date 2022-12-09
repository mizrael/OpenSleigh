using System;

namespace OpenSleigh.Persistence.SQL.Tests
{
    public record DummyState(Guid Id, string Foo, int Bar)
    {
        public static DummyState New() => new DummyState(Guid.NewGuid(), "lorem ipsum", 42);
    }

    public record DummyState2(Guid Id)
    {
        public static DummyState2 New() => new DummyState2(Guid.NewGuid());
    }
}