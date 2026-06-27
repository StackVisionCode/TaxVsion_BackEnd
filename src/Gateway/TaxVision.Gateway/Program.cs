using TaxVision.Gateway.Middleware;
using BuildingBlocks.Common;
using BuildingBlocks.Observability;
using BuildingBlocks.Middleware;
using BuildingBlocks.Security;
using BuildingBlocks.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using BuildingBlocks.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseTaxVisionSerilog("gateway");
builder.Services.AddBuildingBlocks();
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionGatewayRateLimiting();
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "gateway");
builder.Services.AddHttpClient("taxvision-health", client =>
    client.Timeout = TimeSpan.FromSeconds(5));

var authHealth = new Uri(
    new Uri(builder.Configuration[
        "ReverseProxy:Clusters:auth:Destinations:auth1:Address"]!),
    "health/ready").ToString();
var tenantHealth = new Uri(
    new Uri(builder.Configuration[
        "ReverseProxy:Clusters:tenant:Destinations:tenant1:Address"]!),
    "health/ready").ToString();

builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "auth-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [authHealth])
    .AddTypeActivatedCheck<HttpEndpointHealthCheck>(
        "tenant-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: [tenantHealth]);
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();




app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantPropagationMiddleware>();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapReverseProxy();

app.Run();
