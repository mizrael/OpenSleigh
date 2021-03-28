using System;
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

    public record CheckInventory(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static CheckInventory New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }

    public record ProcessShipping(Guid Id, Guid OrderId) : ICommand
    {
        public Guid CorrelationId => this.OrderId;
        public static ProcessShipping New(Guid orderId) => new(Guid.NewGuid(), orderId);
    }
}
