﻿using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Core
{
    public interface IInfrastructureCreator
    {
        Task SetupAsync(IHost host);
    }
    
    public interface IInfrastructureCreator<TM> : IInfrastructureCreator
        where TM : IMessage
    {
    }
}