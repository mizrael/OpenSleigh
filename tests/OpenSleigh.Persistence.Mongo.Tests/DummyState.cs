using System;

namespace OpenSleigh.Persistence.Mongo.Tests
{
    public record DummyState
    {
        public Guid Id { get; init; }
        public string Foo { get; init; }
        public int Bar { get; init; }

        public static DummyState New() 
            => new DummyState()
            {
                Id = Guid.NewGuid(),
                Foo = "lorem ipsum",
                Bar = 42
            };
    }
}