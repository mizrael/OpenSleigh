using OpenSleigh.Transport;

namespace OpenSleigh.Samples.PizzaTracker;

public record PlaceOrder(string CustomerName, string PizzaType) : IMessage;

public record PreparePizza : IMessage;

public record BakePizza : IMessage;

public record QualityCheck : IMessage;

public record SendOutForDelivery : IMessage;

public record ConfirmDelivery : IMessage;
