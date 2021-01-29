using System;
using System.Text.Json.Serialization;
using OpenSleigh.Core.Messaging;

namespace OpenSleigh.Samples.Sample4.Common
{
    public record SaveOrder(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static SaveOrder New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record ProcessCreditCheck(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static ProcessCreditCheck New(Guid orderId) => new (Guid.NewGuid(), orderId);
    }

    public record CrediCheckCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static CrediCheckCompleted New(Guid orderId) => new (Guid.NewGuid(), orderId);
    }

    public record CheckInventory(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static CheckInventory New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record InventoryCheckCompleted(Guid Id, Guid OrderId) : IEvent
    {
        public Guid CorrelationId => this.OrderId;
        public static InventoryCheckCompleted New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record ProcessShipping(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static ProcessShipping New(Guid orderId) => new(Guid.NewGuid(), orderId);
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
