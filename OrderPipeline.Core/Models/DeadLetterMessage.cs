namespace OrderPipeline.Core.Models;

public class DeadLetterMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OriginalCreatedAt { get; set; }
    public DateTime DeadLetteredAt { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public int RetryCount { get; set; }
}
