using OpenSleigh.Core.Messaging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace OpenSleigh.Core
{
    [ExcludeFromCodeCoverage] // only if doesn't get more complex than this
    internal record DefaultCompensationContext<TM>(IMessageContext<TM> MessageContext, Exception Exception) : ICompensationContext<TM>
        where TM : IMessage
    {
        public static ICompensationContext<TM> Build(IMessageContext<TM> messageContext, Exception exception)
            => new DefaultCompensationContext<TM>(messageContext, exception);
    }
}