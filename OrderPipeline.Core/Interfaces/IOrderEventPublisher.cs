using OrderPipeline.Core.Models;

namespace OrderPipeline.Core.Interfaces;

public interface IOrderEventPublisher
{
    Task PublishToKafkaAsync(Order order, OrderEventType eventType);
    Task PublishToServiceBusAsync(Order order, OrderEventType eventType);
}