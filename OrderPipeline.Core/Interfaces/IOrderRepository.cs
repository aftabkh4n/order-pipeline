using OrderPipeline.Core.Models;

namespace OrderPipeline.Core.Interfaces;

public interface IOrderRepository
{
    Task<Order> CreateAsync(Order order);
    Task<Order?> GetByIdAsync(Guid id);
    Task<IEnumerable<Order>> GetAllAsync();
    Task<Order> UpdateStatusAsync(Guid id, OrderStatus status, string? failureReason = null);
}