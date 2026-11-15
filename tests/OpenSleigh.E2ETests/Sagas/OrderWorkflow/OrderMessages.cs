using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas.OrderWorkflow;

public record PlaceOrder(Guid OrderId, string CorrelationId, decimal Amount, IReadOnlyList<string> Skus) : IMessage, IHasCorrelationId;

public record CheckCredit(Guid OrderId, decimal Amount) : IMessage;

public record CreditChecked(Guid OrderId) : IMessage;

public record ReserveStock(Guid OrderId, IReadOnlyList<string> Skus) : IMessage;

/// <summary>the warehouse answers once for every line of the order.</summary>
public record StockReserved(Guid OrderId, string Sku) : IMessage;

public record ShipOrder(Guid OrderId) : IMessage;

public record OrderShipped(Guid OrderId, string TrackingNumber) : IMessage;
