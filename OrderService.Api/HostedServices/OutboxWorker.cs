using Dapper;
using Microsoft.Extensions.Hosting;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Database.Dapper;
using RabbitMQ.Client;
using Serilog;
using System.Text;
using System.Text.Json;

namespace OrderService.Api.HostedServices
{
    public class OutboxWorker : BackgroundService
    {
        private readonly DapperContext _context;

        public OutboxWorker(DapperContext context)
        {
            _context = context;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("📤 OutboxWorker başlatılıyor...");

            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                UserName = "guest",
                Password = "guest"
            };

            try
            {
                // Bağlantı ve kanal
                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                Log.Information("📡 RabbitMQ bağlantısı kuruldu (exchange: order_exchange)");

                // Sadece EXCHANGE tanımlıyoruz (publisher tarafı)
                await channel.ExchangeDeclareAsync(
                    exchange: "order_exchange",
                    type: "direct",
                    durable: true,
                    autoDelete: false,
                    arguments: null,
                    cancellationToken: stoppingToken
                );

                Log.Information("🔄 OutboxWorker çalışmaya hazır.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await ProcessOutboxMessages(channel, stoppingToken);
                    await Task.Delay(3000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "❌ OutboxWorker fatal error");
                throw;
            }
        }

        private async Task ProcessOutboxMessages(IChannel channel, CancellationToken cancellationToken)
        {
            const string sql = @"SELECT * FROM OutboxMessages WHERE Status= 'Pending' LIMIT 10";

            using var conn = _context.CreateConnection();
            var messages = await conn.QueryAsync<OutboxMessage>(sql);

            if (!messages.Any())
                return;

            foreach (var msg in messages)
            {
                try
                {
                    Log.Information(
                        "🚀 Outbox mesajı publish ediliyor | OutboxId={OutboxId} | EventType={EventType}",
                        msg.Id, msg.EventType);

                    var body = Encoding.UTF8.GetBytes(msg.Payload);

                    // Artık DIRECT EXCHANGE'e publish ediyoruz
                    await channel.BasicPublishAsync(
                        exchange: "order_exchange",
                        routingKey: "order.created",
                        mandatory: false,
                        basicProperties: new BasicProperties(),
                        body: body,
                        cancellationToken: cancellationToken
                    );

                    const string updateSql = @"
                        UPDATE OutboxMessages
                        SET Status = 'Processed', ProcessedAt = NOW()
                        WHERE Id = @Id";

                    await conn.ExecuteAsync(updateSql, new { msg.Id });

                    Log.Information(
                        "✅ Outbox mesajı işlendi | OutboxId={OutboxId} | RoutingKey=order.created",
                        msg.Id);
                }
                catch (Exception ex)
                {
                    Log.Error(
                        ex,
                        "❌ Outbox mesajı işlenirken hata oluştu | OutboxId={OutboxId}",
                        msg.Id);
                }
            }
        }
    }
}
