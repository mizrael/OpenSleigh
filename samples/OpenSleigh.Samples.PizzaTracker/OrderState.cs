namespace OpenSleigh.Samples.PizzaTracker;

public record OrderState
{
    public string CustomerName { get; set; } = string.Empty;
    public string PizzaType { get; set; } = string.Empty;
    public string Status { get; set; } = "Received";
    public DateTime OrderedAt { get; set; }
    public DateTime? PreparedAt { get; set; }
    public DateTime? BakedAt { get; set; }
    public DateTime? QualityCheckedAt { get; set; }
    public DateTime? OutForDeliveryAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}
