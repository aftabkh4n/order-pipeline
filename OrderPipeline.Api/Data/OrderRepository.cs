using Microsoft.EntityFrameworkCore;
using OrderPipeline.Core.Interfaces;
using OrderPipeline.Core.Models;

namespace OrderPipeline.Api.Data;

public class OrderRepository : IOrderRepository
{
    private readonly OrderDbContext _context;
    private readonly ILogger<OrderRepository> _logger;

    public OrderRepository(OrderDbContext context, ILogger<OrderRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Order> CreateAsync(Order order)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Order {OrderId} created in database", order.Id);
        return order;
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<IEnumerable<Order>> GetAllAsync()
    {
        return await _context.Orders
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Order> UpdateStatusAsync(Guid id, OrderStatus status, string? failureReason = null)
    {
        var order = await _context.Orders.FindAsync(id)
            ?? throw new KeyNotFoundException($"Order {id} not found");

        order.Status = status;
        order.FailureReason = failureReason;

        if (status == OrderStatus.Fulfilled || status == OrderStatus.Failed)
            order.ProcessedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Order {OrderId} status updated to {Status}", id, status);
        return order;
    }
}