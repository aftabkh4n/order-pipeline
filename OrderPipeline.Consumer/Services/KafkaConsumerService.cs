using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderPipeline.Consumer.Data;
using OrderPipeline.Core.Models;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace OrderPipeline.Consumer.Services;

public class KafkaConsumerService : BackgroundService
{
    private readonly ILogger<KafkaConsumerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;

    public KafkaConsumerService(
        ILogger<KafkaConsumerService> logger,
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Kafka consumer started");

        var config = new ConsumerConfig
        {
            BootstrapServers = _configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = "order-pipeline-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_configuration["Kafka:Topic"] ?? "orders");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result == null) continue;

                _logger.LogInformation(
                    "Consumed message from partition {Partition} at offset {Offset}",
                    result.Partition, result.Offset);

                var orderEvent = JsonSerializer.Deserialize<OrderEvent>(result.Message.Value);
                if (orderEvent == null) continue;

                await ProcessOrderEventAsync(orderEvent);

                consumer.Commit(result);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error consuming Kafka message");
                await Task.Delay(1000, stoppingToken);
            }
        }

        consumer.Close();
        _logger.LogInformation("Kafka consumer stopped");
    }

    private async Task ProcessOrderEventAsync(OrderEvent orderEvent)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderEvent.OrderId);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} not found", orderEvent.OrderId);
            return;
        }

        _logger.LogInformation(
            "Processing order {OrderId} for customer {Customer}",
            order.Id, order.CustomerName);

        order.Status = OrderStatus.Processing;
        await db.SaveChangesAsync();

        await Task.Delay(500);

        order.Status = OrderStatus.Fulfilled;
        order.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await PublishFulfilmentEventAsync(order);

        _logger.LogInformation("Order {OrderId} fulfilled successfully", order.Id);
    }

    private async Task PublishFulfilmentEventAsync(Order order)
    {
        var connectionString = _configuration["ServiceBus:ConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("Service Bus not configured, skipping fulfilment event");
            return;
        }

        await using var client = new ServiceBusClient(connectionString);
        await using var sender = client.CreateSender(
            _configuration["ServiceBus:QueueName"] ?? "order-fulfilment");

        var orderEvent = new OrderEvent
        {
            OrderId = order.Id,
            EventType = OrderEventType.OrderFulfilled,
            Payload = System.Text.Json.JsonSerializer.Serialize(order),
            Source = "OrderPipeline.Consumer"
        };

        var message = new ServiceBusMessage(
            System.Text.Json.JsonSerializer.Serialize(orderEvent))
        {
            MessageId = orderEvent.EventId.ToString(),
            Subject = "OrderFulfilled",
            ContentType = "application/json"
        };

        await sender.SendMessageAsync(message);
        _logger.LogInformation(
            "Fulfilment event published to Service Bus for order {OrderId}", order.Id);
    }
}