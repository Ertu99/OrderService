using OrderService.Api.HostedServices;
using OrderService.Application.Interfaces;
using OrderService.Application.Services;
using OrderService.Infrastructure.Cache;
using OrderService.Infrastructure.Database.Dapper;
using OrderService.Infrastructure.Database.Repositories;
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
        Password = "guest",
        
    };
});

// =======================================
// DAPPER
// =======================================
builder.Services.AddSingleton(new DapperContext(
    builder.Configuration.GetConnectionString("Postgres")
));

// =======================================
// REPOSITORIES
// =======================================
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOutboxRepository, OutboxRepository>();

// =======================================
// APPLICATION SERVICES
// =======================================
builder.Services.AddScoped<OrderAppService>();

// =======================================
// BACKGROUND SERVICES (WORKERS)
// =======================================
builder.Services.AddHostedService<OutboxWorker>();           // OrderCreated publish
builder.Services.AddHostedService<PaymentEventsConsumer>();  // PaymentSucceeded/Failed consume

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
