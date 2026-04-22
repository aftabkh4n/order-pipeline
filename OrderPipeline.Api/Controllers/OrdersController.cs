using Microsoft.AspNetCore.Mvc;
using OrderPipeline.Core.Interfaces;
using OrderPipeline.Core.Models;

namespace OrderPipeline.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _repository;
    private readonly IOrderEventPublisher _publisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderRepository repository,
        IOrderEventPublisher publisher,
        ILogger<OrdersController> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList()
        };

        order.TotalAmount = order.Items.Sum(i => i.TotalPrice);

        var created = await _repository.CreateAsync(order);

        await _publisher.PublishToKafkaAsync(created, OrderEventType.OrderCreated);

        _logger.LogInformation("Order {OrderId} created and published to Kafka", created.Id);

        return CreatedAtAction(nameof(GetOrder), new { id = created.Id }, created);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var order = await _repository.GetByIdAsync(id);
        return order == null ? NotFound() : Ok(order);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders()
    {
        var orders = await _repository.GetAllAsync();
        return Ok(orders);
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusRequest request)
    {
        var order = await _repository.UpdateStatusAsync(id, request.Status, request.FailureReason);
        await _publisher.PublishToServiceBusAsync(order, OrderEventType.OrderFulfilled);
        return Ok(order);
    }
}

public record CreateOrderRequest(
    string CustomerName,
    string CustomerEmail,
    List<OrderItemRequest> Items);

public record OrderItemRequest(
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public record UpdateStatusRequest(
    OrderStatus Status,
    string? FailureReason);