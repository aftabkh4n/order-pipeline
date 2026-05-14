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
    private const int MaxRetries = 3;

    // Normal cadence
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    // Exponential backoff when Kafka is unavailable
    private static readonly TimeSpan KafkaBackoffMin = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KafkaBackoffMax = TimeSpan.FromSeconds(60);
    private TimeSpan _currentBackoff = KafkaBackoffMin;

    // Age-alert threshold
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(60);

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
            bool kafkaHealthy = true;
            try
            {
                kafkaHealthy = await ProcessOutboxMessagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error processing outbox messages");
            }

            if (kafkaHealthy)
            {
                _currentBackoff = KafkaBackoffMin;
                await Task.Delay(PollingInterval, stoppingToken);
            }
            else
            {
                _logger.LogWarning(
                    "Kafka unavailable — backing off {BackoffSeconds}s before next attempt",
                    (int)_currentBackoff.TotalSeconds);

                await Task.Delay(_currentBackoff, stoppingToken);

                // Double for the next consecutive failure, capped at max
                _currentBackoff = TimeSpan.FromSeconds(
                    Math.Min(_currentBackoff.TotalSeconds * 2, KafkaBackoffMax.TotalSeconds));
            }
        }

        _logger.LogInformation("OutboxProcessorService stopped");
    }

    // Returns true when Kafka is reachable (or there is nothing to publish).
    // Returns false when at least one Kafka publish failed so the caller can apply backoff.
    private async Task<bool> ProcessOutboxMessagesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        // Open a transaction so that FOR UPDATE SKIP LOCKED holds row locks until commit,
        // preventing any other poller instance from picking up the same messages.
        await using var transaction = await context.Database.BeginTransactionAsync(stoppingToken);

        var unprocessedMessages = await context.OutboxMessages
            .FromSql($"""
                SELECT * FROM "OutboxMessages"
                WHERE "Processed" = false AND "RetryCount" < {MaxRetries}
                ORDER BY "CreatedAt"
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(stoppingToken);

        if (unprocessedMessages.Count == 0)
        {
            await transaction.CommitAsync(stoppingToken);
            return true;
        }

        // Age alert: warn for every message stuck longer than the threshold.
        var now = DateTime.UtcNow;
        var staleCount = unprocessedMessages.Count(m => m.CreatedAt < now - StaleThreshold);
        if (staleCount > 0)
        {
            var oldestAgeSecs = (now - unprocessedMessages.Min(m => m.CreatedAt)).TotalSeconds;
            _logger.LogWarning(
                "Outbox age alert: {StaleCount} message(s) unprocessed for over {ThresholdSeconds}s; oldest is {OldestAge:F0}s old",
                staleCount,
                (int)StaleThreshold.TotalSeconds,
                oldestAgeSecs);
        }

        _logger.LogInformation("Processing {Count} outbox messages", unprocessedMessages.Count);

        bool anyKafkaFailure = false;

        foreach (var message in unprocessedMessages)
        {
            try
            {
                var orderEvent = JsonSerializer.Deserialize<OrderEvent>(message.Payload)
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize order event for message {message.Id}");

                var order = JsonSerializer.Deserialize<Order>(orderEvent.Payload)
                    ?? throw new InvalidOperationException(
                        $"Failed to deserialize order for message {message.Id}");

                // Publish; mark any exception here as a Kafka failure so the caller
                // can apply backoff — deserialization errors above are not Kafka problems.
                try
                {
                    await _publisher.PublishToKafkaAsync(order, orderEvent.EventType);
                }
                catch (Exception)
                {
                    anyKafkaFailure = true;
                    throw;
                }

                message.Processed = true;
                message.ProcessedAt = now;
                message.Error = null;
                message.RetryCount = 0;

                _logger.LogInformation("Outbox message {MessageId} published successfully", message.Id);
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                // Guard against exceeding the column's max length (500)
                message.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

                _logger.LogWarning(
                    ex,
                    "Failed to process outbox message {MessageId}, retry count: {RetryCount}",
                    message.Id,
                    message.RetryCount);
            }
        }

        await context.SaveChangesAsync(stoppingToken);
        await transaction.CommitAsync(stoppingToken);

        return !anyKafkaFailure;
    }
}
