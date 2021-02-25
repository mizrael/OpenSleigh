using System;
using System.Diagnostics.CodeAnalysis;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface ICompensationContext<TM>
        where TM : IMessage
    {
        IMessageContext<TM> MessageContext { get; }
        Exception Exception { get; }
    }
    
    [ExcludeFromCodeCoverage] // only if doesn't get more complex than this
    internal record DefaultCompensationContext<TM>(IMessageContext<TM> MessageContext, Exception Exception) : ICompensationContext<TM>
        where TM : IMessage
    {
        public static ICompensationContext<TM> Build(IMessageContext<TM> messageContext, Exception exception)
            => new DefaultCompensationContext<TM>(messageContext, exception);
    }
}