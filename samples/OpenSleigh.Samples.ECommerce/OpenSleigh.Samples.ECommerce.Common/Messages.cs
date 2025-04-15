using OpenSleigh.Transport;
using System;

namespace OpenSleigh.Samples.ECommerce.Common;

public record SaveOrder(Guid OrderId) : IMessage;

public record ProcessCreditCheck(Guid OrderId) : IMessage;

public record CheckInventory(Guid OrderId) : IMessage;

public record ProcessShipping(Guid OrderId) : IMessage;
