using System;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample4.Common
{
    public record CrediCheckCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static CrediCheckCompleted New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record InventoryCheckCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static InventoryCheckCompleted New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record ShippingCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static ShippingCompleted New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record OrderSagaCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static OrderSagaCompleted New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }
}
