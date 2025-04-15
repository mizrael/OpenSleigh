using OpenSleigh.Transport;
using System;

namespace OpenSleigh.Samples.ECommerce.Common;

public record CrediCheckCompleted(Guid OrderId) : IMessage;

public record InventoryCheckCompleted( Guid OrderId) : IMessage;

public record ShippingCompleted(Guid OrderId) : IMessage;

public record OrderSagaCompleted(Guid OrderId) : IMessage;
