using Microsoft.Extensions.Hosting;
using OrderService.Application.DTOs.Events;
using OrderService.Application.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using System.Text;
using System.Text.Json;

namespace OrderService.Api.HostedServices
{
    public class PaymentEventsConsumer : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public PaymentEventsConsumer(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("📥 PaymentEventsConsumer başlatılıyor...");

            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            await using var connection = await factory.CreateConnectionAsync(stoppingToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // 1) EXCHANGE DECLARE
            await channel.ExchangeDeclareAsync(
                exchange: "payment_exchange",
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // 2) QUEUE DECLARE
            await channel.QueueDeclareAsync(
                queue: "payment_events",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            // 3) BIND: payment.succeeded
            await channel.QueueBindAsync(
                queue: "payment_events",
                exchange: "payment_exchange",
                routingKey: "payment.succeeded",
                arguments: null,
                cancellationToken: stoppingToken
            );

            // 4) BIND: payment.failed
            await channel.QueueBindAsync(
                queue: "payment_events",
                exchange: "payment_exchange",
                routingKey: "payment.failed",
                arguments: null,
                cancellationToken: stoppingToken
            );

            Log.Information("📥 OrderService → Payment events dinleniyor...");

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.ReceivedAsync += async (sender, ea) =>
            {
                var json = Encoding.UTF8.GetString(ea.Body.ToArray());
                string routingKey = ea.RoutingKey;

                Log.Information(
                    "📨 Payment event alındı | RoutingKey={RoutingKey} | Size={Size}B",
                    routingKey, json.Length
                );

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<OrderAppService>();

                    if (routingKey == "payment.succeeded")
                    {
                        var evt = JsonSerializer.Deserialize<PaymentSucceededEvent>(json);

                        if (evt != null)
                        {
                            Log.Information("💰 Payment Succeeded | OrderId={OrderId}", evt.OrderId);
                            await service.HandlePaymentSucceededAsync(evt);
                        }
                    }
                    else if (routingKey == "payment.failed")
                    {
                        var evt = JsonSerializer.Deserialize<PaymentFailedEvent>(json);

                        if (evt != null)
                        {
                            Log.Warning("❌ Payment Failed | OrderId={OrderId}", evt.OrderId);
                            await service.HandlePaymentFailedAsync(evt);
                        }
                    }
                    else
                    {
                        Log.Warning("⚠ Bilinmeyen routing key alındı: {RoutingKey}", routingKey);
                    }

                    await channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "❌ PaymentEventsConsumer hata aldı | RoutingKey={RoutingKey}",
                        routingKey
                    );

                    await channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
                }
            };

            await channel.BasicConsumeAsync(
                queue: "payment_events",
                autoAck: false,
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            // Worker sonsuz döngüde kalır
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }
}
