using OpenSleigh.Transport;

namespace OpenSleigh.E2ETests.Sagas.OrderWorkflow;

public record PlaceOrder(Guid OrderId, string CorrelationId, decimal Amount, int Quantity) : IMessage, IHasCorrelationId;

public record CheckCredit(Guid OrderId, decimal Amount) : IMessage;

public record CreditChecked(Guid OrderId) : IMessage;

public record ReserveStock(Guid OrderId, int Quantity) : IMessage;

public record StockReserved(Guid OrderId) : IMessage;

public record ShipOrder(Guid OrderId) : IMessage;

public record OrderShipped(Guid OrderId, string TrackingNumber) : IMessage;
