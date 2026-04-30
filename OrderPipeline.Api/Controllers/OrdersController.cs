using Microsoft.AspNetCore.Mvc;
using OrderPipeline.Api.Data;
using OrderPipeline.Core.Interfaces;
using OrderPipeline.Core.Models;
using System.Text.Json;

namespace OrderPipeline.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepository _repository;
    private readonly OrderDbContext _context;
    private readonly IOrderEventPublisher _publisher;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IOrderRepository repository,
        OrderDbContext context,
        IOrderEventPublisher publisher,
        ILogger<OrdersController> logger)
    {
        _repository = repository;
        _context = context;
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

        // Write order and outbox message in the same transaction
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Create outbox message for Kafka publishing
            var orderEvent = new OrderEvent
            {
                OrderId = order.Id,
                EventType = OrderEventType.OrderCreated,
                Payload = JsonSerializer.Serialize(order),
                Source = "OrderPipeline.Api"
            };

            var outboxMessage = new OutboxMessage
            {
                OrderId = order.Id,
                EventType = OrderEventType.OrderCreated.ToString(),
                Payload = JsonSerializer.Serialize(orderEvent)
            };

            _context.OutboxMessages.Add(outboxMessage);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Order {OrderId} created and outbox message queued", order.Id);

            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating order");
            throw;
        }
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