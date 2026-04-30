namespace OrderPipeline.Core.Models;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public bool Processed { get; set; } = false;
    public int RetryCount { get; set; } = 0;
    public string? Error { get; set; }
}
