using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Middleware;
using JasperFx.CodeGeneration.Model;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Infrastructure;
using TaxVision.Auth.Infrastructure.Security;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;
using Wolverine.EntityFrameworkCore;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using BuildingBlocks.Security;
using TaxVision.Auth.Infrastructure.Persistence;
using BuildingBlocks.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Text.Json.Serialization;
using Serilog;
using TaxVision.Auth.Api.Bootstrap;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("auth-service");
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddBuildingBlocks();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.Configure<PlatformBootstrapOptions>(
    builder.Configuration.GetSection(PlatformBootstrapOptions.SectionName));
builder.Services.AddHostedService<PlatformAdminBootstrapService>();

builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "auth-service");

var authRabbitUri = new Uri(builder.Configuration["RabbitMq:Uri"]
    ?? throw new InvalidOperationException("RabbitMq:Uri is missing."));
var authRedis = HostPort.Parse(
    builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
    6379);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AuthDbContext>("sql-server", tags: ["ready"])
    .AddCheck(
        "redis",
        new TcpEndpointHealthCheck(authRedis.Host, authRedis.Port),
        tags: ["ready"])
    .AddCheck(
        "rabbitmq",
        new TcpEndpointHealthCheck(authRabbitUri.Host, authRabbitUri.Port),
        tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(LoginHandler).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;

    var rabbitUri = builder.Configuration["RabbitMq:Uri"]
        ?? throw new InvalidOperationException("RabbitMq:Uri is missing.");

    var sqlConn = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseRabbitMq(new Uri(rabbitUri)).AutoProvision();
    options.PersistMessagesWithSqlServer(sqlConn);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions()
        .WithDbContextAbstraction<IUnitOfWork, AuthDbContext>();
    options.Policies.AutoApplyTransactions();

    options.PublishMessage<UserRegisteredIntegrationEvent>()
        .ToRabbitExchange("taxvision-events");

    options.ListenToRabbitQueue("auth-tenant-events", queue =>
    {
        queue.BindExchange("taxvision-events", string.Empty);
    }).UseDurableInbox();

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
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "Auth API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapControllers();

app.Run();
