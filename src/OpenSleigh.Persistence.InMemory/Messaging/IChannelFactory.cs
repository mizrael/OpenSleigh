using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OpenSleigh.Core.Messaging;

[assembly: InternalsVisibleTo("OpenSleigh.Persistence.InMemory.Tests")]
namespace OpenSleigh.Persistence.InMemory.Messaging
{
    public interface IChannelFactory
    {
        ChannelWriter<TM> GetWriter<TM>() where TM : IMessage;
    }
}