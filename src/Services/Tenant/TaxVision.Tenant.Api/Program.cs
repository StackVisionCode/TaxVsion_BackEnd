using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Middleware;
using JasperFx.CodeGeneration.Model;
using Serilog;
using Serilog.Sinks.MSSqlServer;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Infrastructure;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// 1) Structured loggin with Serilog
builder.Host.UseSerilog((context, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("service", "tenant-service")
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "Logs", "tenant-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
// 2) Services Shared plus Services's Infrastructure
builder.Services.AddBuildingBlocks();
//  Added Cache's Services
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddTenantInfrastructure(builder.Configuration);

// 3)
builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(CreateTenantHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri = builder.Configuration["RabbitMq:Uri"]
        ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();

    options.PublishMessage<TenantCreatedIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    options.Policies.OnException<Exception>()
        .RetryWithCooldown(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15));
});
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(a =>
    {
        a.SwaggerEndpoint("/openapi/v1.json", "API v1");
    });
}
//4) Middleware's Pipe Line (the order of this, it's important)
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
