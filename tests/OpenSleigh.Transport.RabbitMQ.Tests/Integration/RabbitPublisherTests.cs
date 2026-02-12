using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenSleigh.Outbox;
using OpenSleigh.Transport.RabbitMQ.Tests.Fixtures;
using OpenSleigh.Utils;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenSleigh.Transport.RabbitMQ.Tests.Integration;

[Category("Integration")]
[Trait("Category", "Integration")]
public class RabbitPublisherTests : IClassFixture<RabbitFixture>
{
    private readonly RabbitFixture _fixture;

    public RabbitPublisherTests(RabbitFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAsync_should_publish_message()
    {
        var sagaContext = Substitute.For<ISagaInstance>();
        sagaContext.CorrelationId.Returns(Guid.NewGuid().ToString());
        sagaContext.TriggerMessageId.Returns(Guid.NewGuid().ToString());
        sagaContext.InstanceId.Returns(Guid.NewGuid().ToString());

        var envelope = MessageEnvelope.Create(new FakeSagaStarter(), sagaContext);

        using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var connection = await _fixture.ConnectionFactory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        var queueRef = _fixture.CreateQueueReference();

        var received = false;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, evt) =>
        {
            Assert.NotNull(evt.BasicProperties.Headers);
            Assert.NotEmpty(evt.BasicProperties.Headers);
            Assert.True(evt.BasicProperties.Headers.ContainsKey(nameof(MessageEnvelope.SenderId)));
            Assert.True(evt.BasicProperties.Headers.ContainsKey(nameof(MessageEnvelope.CreatedAt)));
            Assert.True(evt.BasicProperties.Headers.ContainsKey(nameof(MessageEnvelope.MessageType)));
            Assert.Equal(envelope.CorrelationId, evt.BasicProperties.CorrelationId);
            Assert.Equal(envelope.MessageId, evt.BasicProperties.MessageId);
            Assert.Equal(Encoding.UTF8.GetBytes(typeof(FakeSagaStarter).FullName), (byte[])evt.BasicProperties.Headers[nameof(MessageEnvelope.MessageType)]);
            Assert.Equal(Encoding.UTF8.GetBytes(envelope.CreatedAt.ToString()), (byte[])evt.BasicProperties.Headers[nameof(MessageEnvelope.CreatedAt)]);
            Assert.Equal(Encoding.UTF8.GetBytes(envelope.SenderId), (byte[])evt.BasicProperties.Headers[nameof(MessageEnvelope.SenderId)]);

            Assert.False(evt.Body.IsEmpty);

            var serializer = new JsonSerializer();
            var message = serializer.Deserialize<FakeSagaStarter>(evt.Body.Span);
            Assert.NotNull(message);
            Assert.Equivalent(envelope.Message, message);

            received = true;
            tokenSource.Cancel();
        };

        await channel.EnsureTopologyAsync(queueRef, _fixture.RabbitConfiguration);

        await channel.BasicConsumeAsync(
            queue: queueRef.QueueName,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer,
            cancellationToken: CancellationToken.None);

        var logger = Substitute.For<ILogger<RabbitPublisher>>();

        var channelFactory = Substitute.For<IChannelFactory>();
        channelFactory.GetPublishChannelAsync(Arg.Any<CancellationToken>()).Returns(channel);

        var queueRefFactory = Substitute.For<IQueueReferenceFactory>();
        queueRefFactory.Create(envelope).Returns(queueRef);

        var sut = new RabbitPublisher(queueRefFactory, _fixture.RabbitConfiguration, channelFactory, logger, new JsonSerializer());
        await sut.PublishAsync(envelope);

        while (!tokenSource.IsCancellationRequested)
            await Task.Delay(10);

        Assert.True(received);
    }
}
