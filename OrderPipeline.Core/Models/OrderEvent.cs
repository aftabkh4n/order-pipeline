namespace OrderPipeline.Core.Models;

public class OrderEvent
{
    public Guid EventId { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public OrderEventType EventType { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
}

public enum OrderEventType
{
    OrderCreated,
    OrderProcessing,
    OrderFulfilled,
    OrderFailed,
    OrderCancelled
}