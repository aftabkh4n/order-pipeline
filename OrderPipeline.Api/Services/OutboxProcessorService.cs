using Microsoft.EntityFrameworkCore;
using OrderPipeline.Api.Data;
using OrderPipeline.Core.Interfaces;
using OrderPipeline.Core.Models;
using System.Text.Json;

namespace OrderPipeline.Api.Services;

public class OutboxProcessorService : BackgroundService
{
    private readonly ILogger<OutboxProcessorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOrderEventPublisher _publisher;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private const int MaxRetries = 3;

    public OutboxProcessorService(
        ILogger<OutboxProcessorService> logger,
        IServiceProvider serviceProvider,
        IOrderEventPublisher publisher)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxProcessorService starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        _logger.LogInformation("OutboxProcessorService stopped");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var unprocessedMessages = await context.OutboxMessages
            .Where(m => !m.Processed && m.RetryCount < MaxRetries)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(stoppingToken);

        if (unprocessedMessages.Count == 0)
            return;

        _logger.LogInformation("Processing {Count} unprocessed outbox messages", unprocessedMessages.Count);

        foreach (var message in unprocessedMessages)
        {
            try
            {
                // Reconstruct the order event
                var orderEvent = JsonSerializer.Deserialize<OrderEvent>(message.Payload);
                if (orderEvent == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize order event for message {message.Id}");
                }

                // Publish to Kafka
                await _publisher.PublishToKafkaAsync(
                    JsonSerializer.Deserialize<Order>(orderEvent.Payload)
                        ?? throw new InvalidOperationException("Failed to deserialize order"),
                    orderEvent.EventType);

                // Mark as processed
                message.Processed = true;
                message.ProcessedAt = DateTime.UtcNow;
                message.Error = null;
                message.RetryCount = 0;

                _logger.LogInformation("Outbox message {MessageId} published successfully", message.Id);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message;

                _logger.LogWarning(
                    ex,
                    "Failed to process outbox message {MessageId}, retry count: {RetryCount}",
                    message.Id,
                    message.RetryCount);
            }
        }

        await context.SaveChangesAsync(stoppingToken);
    }
}
