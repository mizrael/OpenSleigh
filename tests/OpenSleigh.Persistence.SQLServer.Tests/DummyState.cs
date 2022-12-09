﻿using System;
using OpenSleigh.Core;

namespace OpenSleigh.Persistence.SQLServer.Tests
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


    public record DummyState2 : SagaState
    {
        public DummyState2(Guid id) : base(id) { }
        
        public static DummyState2 New() => new DummyState2(Guid.NewGuid());
    }
}