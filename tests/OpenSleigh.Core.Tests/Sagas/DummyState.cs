using System;

namespace OpenSleigh.Core.Tests.Sagas
{
    public record DummyState : SagaState
    {
        public DummyState(Guid id, string foo, int bar) : base(id)
        {
            Foo = foo;
            Bar = bar;
        }

        public string Foo { get; }
        public int Bar { get; }

        public static DummyState New() => new DummyState(Guid.NewGuid(), "lorem ipsum", 42);
    }
}