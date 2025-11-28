using OrderService.Application.DTOs;
using OrderService.Application.DTOs.Events;
using OrderService.Application.Interfaces;
using OrderService.Application.Redis;
using OrderService.Domain.Entities;
using Serilog;
using System.Text.Json;

namespace OrderService.Application.Services
{
    public class OrderAppService
    {
        private readonly IOrderRepository _repo;
        private readonly IOutboxRepository _outboxRepo;
        private readonly IRedisCacheService _cache;

        public OrderAppService(IOrderRepository repo, IOutboxRepository outboxRepo, IRedisCacheService cache)
        {
            _repo = repo;
            _outboxRepo = outboxRepo;
            _cache = cache;
        }

        // ============================================
        // CREATE ORDER  (Idempotency + Outbox Pattern)
        // ============================================
        public async Task<int> CreateOrderAsync(CreateOrderDto dto, Guid eventId)
        {
            Log.Information("🚀 OrderService booted successfully at {Time}", DateTime.Now);


            string idemKey = CacheKeys.OrderIdempotency(eventId.ToString());

            // 1) Redis Idempotency Check
            var cachedOrderId = await _cache.GetAsync<int?>(idemKey);
            if (cachedOrderId.HasValue)
            {
                Log.Warning(
                     "Idempotency HIT | Returning existing OrderId={OrderId} | EventId={EventId}",
                     cachedOrderId.Value, eventId
                 );
                return cachedOrderId.Value;
            }

            if (dto.TotalAmount <= 0)
                throw new Exception("Order amount must be greater than zero.");

            // 2) DB Insert
            var order = new Order
            {
                CustomerName = dto.CustomerName,
                TotalAmount = dto.TotalAmount,
                Status = "PendingPayment",    
                CreatedAt = DateTime.UtcNow
            };

            var orderId = await _repo.CreateAsync(order);

            // 3) Redis → Save Idempotency Key
            await _cache.SetAbsoluteAsync(idemKey, orderId, 60);

            // 4) Create Event (OrderCreated)
            var orderCreatedEvent = new OrderCreatedEvent
            {
                EventId = eventId,
                OrderId = orderId,
                CustomerName = order.CustomerName,
                TotalAmount = order.TotalAmount
            };

            string payload = JsonSerializer.Serialize(orderCreatedEvent);

            var outbox = new OutboxMessage
            {
                EventType = "OrderCreated",   // DIRECT exchange routing = order.created
                Payload = payload,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            await _outboxRepo.AddAsync(outbox);

            Log.Information(
                 "Outbox Message Created | EventType=OrderCreated | OrderId={OrderId} | EventId={EventId}",
                 orderId, eventId
                    );

            return orderId;
        }

        // ============================================
        // GET ORDER (Cache-Aside Pattern)
        // ============================================
        public async Task<OrderDto?> GetByIdAsync(int id)
        {
            var cacheKey = CacheKeys.OrderDetails(id);

            // 1) Cache Check
            var cached = await _cache.GetAsync<OrderDto>(cacheKey);
            if (cached != null)
            {
                Log.Information("Cache HIT | OrderId={OrderId}", id);
                return cached;
            }

            // 2) DB Query
            var order = await _repo.GetByIdAsync(id);
            if (order == null)
                return null;

            // 3) Map to DTO
            var dto = new OrderDto
            {
                Id = order.Id,
                CustomerName = order.CustomerName,
                TotalAmount = order.TotalAmount,
                Status = order.Status
            };

            // 4) Cache Add
            await _cache.SetAbsoluteAsync(cacheKey, dto, 10);

            Log.Information("Cache SET | OrderId={OrderId}", id);

            return dto;
        }

        // ============================================
        // SAGA → PAYMENT SUCCEEDED
        // ============================================
        public async Task HandlePaymentSucceededAsync(PaymentSucceededEvent evt)
        {
            await _repo.UpdateStatusAsync(evt.OrderId, "Paid");

            Console.WriteLine($"✅ Order {evt.OrderId} ödeme başarılı → Status = Paid");

            // Cache Invalidate
            await _cache.RemoveAsync(CacheKeys.OrderDetails(evt.OrderId));
        }

        // ============================================
        // SAGA → PAYMENT FAILED
        // ============================================
        public async Task HandlePaymentFailedAsync(PaymentFailedEvent evt)
        {
            await _repo.UpdateStatusAsync(evt.OrderId, "Cancelled");

            Log.Warning(
              "Order Payment FAILED | OrderId={OrderId} | Status=Cancelled",
              evt.OrderId
          );

            // Cache Invalidate
            await _cache.RemoveAsync(CacheKeys.OrderDetails(evt.OrderId));
        }
    }
}
