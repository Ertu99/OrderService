using Microsoft.Extensions.Hosting;
using PaymentService.Application.DTOs.Events;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Redis;
using PaymentService.Application.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using System.Text;
using System.Text.Json;

namespace PaymentService.Api.HostedServices
{
    public class OrderCreatedConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public OrderCreatedConsumer(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            Log.Information("💳 OrderCreatedConsumer started. Listening for order.created events...");

            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            await using var connection = await factory.CreateConnectionAsync(stoppingToken);
            await using var channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: false,
                    publisherConfirmationTrackingEnabled: false
                ),
                cancellationToken: stoppingToken
            );

            // 1) EXCHANGE tanımı
            await channel.ExchangeDeclareAsync(
                exchange: "order_exchange",
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // 2) QUEUE tanımı
            await channel.QueueDeclareAsync(
                queue: "order_events",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // 3) BIND → queue + exchange + routing key
            await channel.QueueBindAsync(
                queue: "order_events",
                exchange: "order_exchange",
                routingKey: "order.created",
                arguments: null,
                cancellationToken: stoppingToken
            );

            Log.Information("📥 OrderCreatedConsumer is now subscribed to 'order_events' queue.");

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                string json = Encoding.UTF8.GetString(ea.Body.ToArray());

                try
                {
                    var evt = JsonSerializer.Deserialize<OrderCreatedEvent>(json);

                    if (evt == null)
                    {
                        Log.Warning("⚠️ OrderCreated event NULL geldi! DeliveryTag={DeliveryTag}",
                            ea.DeliveryTag);

                        await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                        return;
                    }

                    using var scope = _scopeFactory.CreateScope();
                    var paymentService = scope.ServiceProvider.GetRequiredService<PaymentAppService>();
                    var cache = scope.ServiceProvider.GetRequiredService<IRedisCacheService>();

                    // ==============================
                    //       IDEMPOTENCY
                    // ==============================

                    var idemKey = CacheKeys.PaymentIdempotency(evt.EventId.ToString());
                    var isFirst = await cache.TrySetIdempotencyKeyAsync(idemKey);

                    if (!isFirst)
                    {
                        Log.Warning(
                            "🔁 Duplicate OrderCreated event ignored | EventId={EventId} OrderId={OrderId}",
                            evt.EventId, evt.OrderId);

                        await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                        return;
                    }

                    Log.Information(
                        "🟢 Idempotency passed | EventId={EventId} OrderId={OrderId}",
                        evt.EventId, evt.OrderId);

                    // ==============================
                    //       PROCESS PAYMENT
                    // ==============================

                    await paymentService.ProcessPaymentAsync(evt);

                    Log.Information(
                        "💰 Payment processed successfully | OrderId={OrderId}",
                        evt.OrderId);

                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "❌ Error while processing OrderCreated event | Event JSON={JsonPayload}",
                        json
                    );

                    await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
                }
            };

            // Consumer başlat
            await channel.BasicConsumeAsync(
                queue: "order_events",
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
