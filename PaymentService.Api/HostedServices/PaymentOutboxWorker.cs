using Dapper;
using Microsoft.Extensions.Hosting;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Database.Dapper;
using RabbitMQ.Client;
using Serilog;
using System.Text;

namespace PaymentService.Api.HostedServices
{
    public class PaymentOutboxWorker : BackgroundService
    {
        private readonly DapperContext _context;

        public PaymentOutboxWorker(DapperContext context)
        {
            _context = context;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("🚀 PaymentOutboxWorker started.");

            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            await using var connection = await factory.CreateConnectionAsync(stoppingToken);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // DIRECT EXCHANGE
            await channel.ExchangeDeclareAsync(
                exchange: "payment_exchange",
                type: "direct",
                durable: true,
                autoDelete: false,
                arguments: null,
                cancellationToken: stoppingToken
            );

            Log.Information("📤 PaymentOutboxWorker is running and monitoring Pending outbox messages...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxMessages(channel, stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ PaymentOutboxWorker exception occurred");
                }

                await Task.Delay(3000, stoppingToken);
            }
        }

        private async Task ProcessOutboxMessages(IChannel channel, CancellationToken stoppingToken)
        {
            const string sql = @"
                SELECT * FROM OutboxMessages
                WHERE Status = 'Pending'
                ORDER BY CreatedAt
                LIMIT 10";

            using var conn = _context.CreateConnection();
            var messages = await conn.QueryAsync<OutboxMessage>(sql);

            foreach (var msg in messages)
            {
                try
                {
                    var body = Encoding.UTF8.GetBytes(msg.Payload);

                    var routingKey = msg.EventType switch
                    {
                        "PaymentSucceeded" => "payment.succeeded",
                        "PaymentFailed" => "payment.failed",
                        _ => "payment.unknown"
                    };

                    // Publish
                    await channel.BasicPublishAsync(
                        exchange: "payment_exchange",
                        routingKey: routingKey,
                        mandatory: false,
                        basicProperties: new BasicProperties(),
                        body: body,
                        cancellationToken: stoppingToken
                    );

                    const string update = @"
                        UPDATE OutboxMessages
                        SET Status = 'Processed', ProcessedAt = NOW()
                        WHERE Id = @Id";

                    await conn.ExecuteAsync(update, new { msg.Id });

                    Log.Information(
                        "📨 Payment event published | OutboxId={OutboxId} | EventType={EventType} | RoutingKey={RoutingKey}",
                        msg.Id, msg.EventType, routingKey
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "❌ Error publishing outbox event | OutboxId={OutboxId} | EventType={EventType}",
                        msg.Id, msg.EventType
                    );
                }
            }
        }
    }
}
