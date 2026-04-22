using Azure.Messaging.ServiceBus;
using Confluent.Kafka;
using OrderPipeline.Core.Interfaces;
using OrderPipeline.Core.Models;
using System.Text.Json;

namespace OrderPipeline.Api.Services;

public class OrderEventPublisher : IOrderEventPublisher, IAsyncDisposable
{
    private readonly ILogger<OrderEventPublisher> _logger;
    private readonly IProducer<string, string> _kafkaProducer;
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly ServiceBusSender? _sender;
    private readonly string _kafkaTopic;

    public OrderEventPublisher(
    ILogger<OrderEventPublisher> logger,
    IConfiguration configuration)
    {
        _logger = logger;
        _kafkaTopic = configuration["Kafka:Topic"] ?? "orders";

        var kafkaConfig = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092"
        };
        _kafkaProducer = new ProducerBuilder<string, string>(kafkaConfig).Build();

        var serviceBusConnection = configuration["ServiceBus:ConnectionString"] ?? string.Empty;
        if (!string.IsNullOrEmpty(serviceBusConnection))
        {
            _serviceBusClient = new ServiceBusClient(serviceBusConnection);
            _sender = _serviceBusClient.CreateSender(
                configuration["ServiceBus:QueueName"] ?? "order-fulfilment");
        }
    }

    public async Task PublishToKafkaAsync(Order order, OrderEventType eventType)
    {
        var orderEvent = new OrderEvent
        {
            OrderId = order.Id,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(order),
            Source = "OrderPipeline.Api"
        };

        var message = new Message<string, string>
        {
            Key = order.Id.ToString(),
            Value = JsonSerializer.Serialize(orderEvent)
        };

        var result = await _kafkaProducer.ProduceAsync(_kafkaTopic, message);
        _logger.LogInformation(
            "Order {OrderId} published to Kafka topic {Topic} at offset {Offset}",
            order.Id, _kafkaTopic, result.Offset);
    }

    public async Task PublishToServiceBusAsync(Order order, OrderEventType eventType)
    {
        if (_sender == null)
        {
            _logger.LogWarning("Service Bus not configured, skipping publish for order {OrderId}", order.Id);
            return;
        }

        var orderEvent = new OrderEvent
        {
            OrderId = order.Id,
            EventType = eventType,
            Payload = JsonSerializer.Serialize(order),
            Source = "OrderPipeline.Api"
        };

        var message = new ServiceBusMessage(JsonSerializer.Serialize(orderEvent))
        {
            MessageId = orderEvent.EventId.ToString(),
            Subject = eventType.ToString(),
            ContentType = "application/json"
        };

        await _sender.SendMessageAsync(message);
        _logger.LogInformation(
            "Order {OrderId} published to Service Bus for {EventType}",
            order.Id, eventType);
    }

    public async ValueTask DisposeAsync()
    {
        _kafkaProducer.Dispose();
        await _sender.DisposeAsync();
        await _serviceBusClient.DisposeAsync();
    }
}