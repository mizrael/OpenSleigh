﻿using OpenSleigh.Transport;

namespace OpenSleigh.Samples.Sample1
{
    public record StartSaga() : IMessage { }

    public record ProcessMySaga() : IMessage { }

    public record MySagaCompleted() : IMessage { }
}
