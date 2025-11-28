using PaymentService.Application.DTOs.Events;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Redis;
using PaymentService.Domain.Entities;
using Serilog;
using System.Text.Json;

namespace PaymentService.Application.Services
{
    public class PaymentAppService
    {
        private readonly IPaymentRepository _paymentRepo;
        private readonly IOutboxRepository _outboxRepo;
        private readonly IRedisCacheService _cache;

        public PaymentAppService(IPaymentRepository paymentRepo,
                                 IOutboxRepository outboxRepo,
                                 IRedisCacheService cache)
        {
            _paymentRepo = paymentRepo;
            _outboxRepo = outboxRepo;
            _cache = cache;
        }

        // ===========================================================
        // MAIN PAYMENT PROCESS
        // ===========================================================
        public async Task ProcessPaymentAsync(OrderCreatedEvent evt)
        {
            Log.Information("💳 PaymentService booted successfully at {Time}", DateTime.Now);

            Log.Information(
               "💳 Payment process started | OrderId={OrderId} | Amount={Amount}",
               evt.OrderId, evt.TotalAmount);

            bool isSuccess = Random.Shared.Next(0, 100) >= 30; // %70 success

            if (isSuccess)
            {
                await HandleSuccess(evt);

                // Redis cache → PaymentService’in kendi result üretimi
                await _cache.SetPaymentResultAsync(
                    CacheKeys.PaymentResult(evt.OrderId),
                    new PaymentResultCacheDto
                    {
                        OrderId = evt.OrderId,
                        Status = "PaymentSucceeded"
                    },
                    minutes: 30
                );

                Log.Information(
                    "🧩 Payment result cached | OrderId={OrderId} | Status=PaymentSucceeded",
                    evt.OrderId
                );
            }
            else
            {
                await HandleFail(evt);

                await _cache.SetPaymentResultAsync(
                    CacheKeys.PaymentResult(evt.OrderId),
                    new PaymentResultCacheDto
                    {
                        OrderId = evt.OrderId,
                        Status = "PaymentFailed"
                    },
                    minutes: 30
                );
                Log.Warning(
                   "🧩 Payment result cached | OrderId={OrderId} | Status=PaymentFailed",
                   evt.OrderId
                    );
            }
        }

        // ===========================================================
        // PAYMENT SUCCESS
        // ===========================================================
        private async Task HandleSuccess(OrderCreatedEvent evt)
        {
            // 1) DB Insert
            var payment = new Payment
            {
                OrderId = evt.OrderId,
                Amount = evt.TotalAmount,
                Status = "Succeeded",
                CreatedAt = DateTime.UtcNow
            };

            await _paymentRepo.AddAsync(payment);

            Log.Information(
               "💰 Payment SUCCESS → DB inserted | OrderId={OrderId} | Amount={Amount}",
               evt.OrderId, evt.TotalAmount
           );

            // 2) Event
            var successEvent = new PaymentSucceededEvent
            {
                OrderId = evt.OrderId,
                Amount = evt.TotalAmount
            };

            string json = JsonSerializer.Serialize(successEvent);

            // 3) Outbox
            var outbox = new OutboxMessage
            {
                EventType = "PaymentSucceeded", // routing key = payment.succeeded
                Payload = json,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            await _outboxRepo.AddAsync(outbox);

            Log.Information(
             "📤 Outbox event created | Type=PaymentSucceeded | OrderId={OrderId}",
             evt.OrderId
         );
        }

        // ===========================================================
        // PAYMENT FAIL
        // ===========================================================
        private async Task HandleFail(OrderCreatedEvent evt)
        {
            // 1) DB Insert
            var payment = new Payment
            {
                OrderId = evt.OrderId,
                Amount = evt.TotalAmount,
                Status = "Failed",
                CreatedAt = DateTime.UtcNow
            };

            await _paymentRepo.AddAsync(payment);

            Log.Warning(
               "❌ Payment FAIL → DB inserted | OrderId={OrderId}",
               evt.OrderId
           );

            // 2) Event
            var failEvent = new PaymentFailedEvent
            {
                OrderId = evt.OrderId,
                Reason = "Insufficient balance" // örnek
            };

            string json = JsonSerializer.Serialize(failEvent);

            // 3) Outbox
            var outbox = new OutboxMessage
            {
                EventType = "PaymentFailed", // routing key = payment.failed
                Payload = json,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            await _outboxRepo.AddAsync(outbox);

            Log.Warning(
                "📤 Outbox event created | Type=PaymentFailed | OrderId={OrderId}",
                evt.OrderId
            );
        }
    }

    // ===========================================================
    // CACHE DTO
    // ===========================================================
    public class PaymentResultCacheDto
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = "";
    }
}
