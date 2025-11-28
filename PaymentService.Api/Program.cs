using PaymentService.Api.HostedServices;
using PaymentService.Application.Interfaces;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Cache;
using PaymentService.Infrastructure.Database.Dapper;
using PaymentService.Infrastructure.Database.Repositories;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Serilog setup - Configuration'dan oku (appsettings.json'dan)
builder.Host.UseSerilog((context, loggerConfig) =>
    loggerConfig.ReadFrom.Configuration(context.Configuration)
);

// =======================================
// CONTROLLERS
// =======================================
builder.Services.AddControllers();

// =======================================
// REDIS
// =======================================
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));

builder.Services.AddScoped<IRedisCacheService, RedisCacheService>();

// =======================================
// RABBITMQ ConnectionFactory 
// =======================================
builder.Services.AddSingleton(sp =>
{
    return new ConnectionFactory
    {
        HostName = "localhost",
        UserName = "guest",
        Password = "guest"
    };
});

// =======================================
// DAPPER
// =======================================
builder.Services.AddSingleton(new DapperContext(
    builder.Configuration.GetConnectionString("Postgres")
));

// =======================================
// APPLICATION SERVICES
// =======================================
builder.Services.AddScoped<PaymentAppService>();

// =======================================
// REPOSITORIES
// =======================================
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

// =======================================
// BACKGROUND SERVICES (WORKERS)
// =======================================
builder.Services.AddHostedService<OrderCreatedConsumer>(); // order.created → payment oluşturur
builder.Services.AddHostedService<PaymentOutboxWorker>();  // payment events → publish

// =======================================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// =======================================

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
