using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Hosting;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Infrastructure.Redis;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging;
using BuildingBlocks.Permissions;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using BuildingBlocks.Web.Tenancy;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using TaxVision.FinancialInsights.Infrastructure.Permissions;
using TaxVision.FinancialInsights.Api.Events;
using TaxVision.FinancialInsights.Api.Imports;
using TaxVision.FinancialInsights.Api.Processing;
using TaxVision.FinancialInsights.Infrastructure.Processing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);
var sql = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(sql)) throw new InvalidOperationException("ConnectionStrings:Default is required.");
var rabbit = builder.Configuration["RabbitMq:Uri"];
if (string.IsNullOrWhiteSpace(rabbit)) throw new InvalidOperationException("RabbitMq:Uri is required.");
builder.Services.AddBuildingBlocks();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddDbContext<FinancialInsightsDbContext>(o => o.UseSqlServer(sql));
builder.Services.Configure<FinancialProcessingOptions>(builder.Configuration.GetSection(FinancialProcessingOptions.Section));
builder.Services.Configure<OpenAiFinancialOptions>(builder.Configuration.GetSection(OpenAiFinancialOptions.Section));
builder.Services.AddScoped<LocalStatementExtractor>();
builder.Services.AddHttpClient<CloudStorageStatementSource>(http =>
{
    if (Uri.TryCreate(builder.Configuration["Services:CloudStorage:BaseUrl"], UriKind.Absolute, out var uri))
        http.BaseAddress = uri;
    http.Timeout = TimeSpan.FromSeconds(60);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<OpenAiStatementClient>(http =>
{
    http.BaseAddress = new Uri("https://api.openai.com/v1/");
    http.Timeout = TimeSpan.FromSeconds(120);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<FinancialOcrWorker>();
builder.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FinancialInsightsDbContext>());
builder.Services.AddScoped<FinancialUserPermissionsReader>();
builder.Services.AddScoped<IUserPermissionsProjectionReader>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
builder.Services.AddScoped<IUserPermissionsProjectionWriter>(sp => sp.GetRequiredService<FinancialUserPermissionsReader>());
builder.Services.AddCachedTenantEntitlementModulesReader<FinancialTenantModulesReader>();
builder.Services.AddScoped<FinancialTenantPlanCodeReader>();
builder.Services.AddScoped(sp => new CachedTenantPlanCodeReader(
    sp.GetRequiredService<BuildingBlocks.Caching.ICacheService>(),
    sp.GetRequiredService<FinancialTenantPlanCodeReader>()));
builder.Services.AddScoped<FinancialEntitlementsCacheInvalidator>();
builder.Services.AddHttpClient<IServiceTokenAcquirer, FinancialServiceTokenAcquirer>(http =>
{
    var authUrl = builder.Configuration["Services:Auth:BaseUrl"];
    if (Uri.TryCreate(authUrl, UriKind.Absolute, out var uri)) http.BaseAddress = uri;
    http.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<HttpPlanRateLimitReader>(http =>
{
    var subscriptionUrl = builder.Configuration["SubscriptionClient:BaseUrl"];
    if (Uri.TryCreate(subscriptionUrl, UriKind.Absolute, out var uri)) http.BaseAddress = uri;
    http.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<IPermissionsSnapshotClient, FinancialPermissionsSnapshotClient>(http =>
{
    if (Uri.TryCreate(builder.Configuration["Services:Auth:BaseUrl"], UriKind.Absolute, out var uri))
        http.BaseAddress = uri;
    http.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddModuleGate(builder.Configuration);
builder.Services.AddUserPermissionsSource(builder.Configuration, typeof(Program).Assembly);
builder.Services.AddTaxVisionRedis(builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis is required."));
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<ITenantPlanCodeReader, ScopedTenantPlanCodeReader>();
    builder.Services.AddSingleton<IPlanRateLimitReader, ScopedPlanRateLimitReader>();
}
builder.Services.AddTieredRateLimiting();
builder.Services.AddHealthChecks().AddDbContextCheck<FinancialInsightsDbContext>("sql-server", tags: ["ready"]);
builder.Services.AddControllers().AddActorTypeAuthorization();
builder.Services.AddTransient<UpstreamImportAccess>();
builder.Services.AddHttpClient("financial-upstream")
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(typeof(FileScanConsumer).Assembly);
    options.UseTaxVisionRabbitMq(new Uri(rabbit));
    options.PersistMessagesWithSqlServer(sql);
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, FinancialInsightsDbContext>();
    options.Policies.AutoApplyTransactions();
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.Policies.ForMessagesOfType<IIntegrationEvent>().AddMiddleware(typeof(IntegrationEventTenantMiddleware));
    options.ListenToRabbitQueue("financial-insights-events", q => q.BindExchange("taxvision-events", string.Empty)).UseDurableInbox();
});
var app = builder.Build();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<JwtTenantContextMiddleware>();
app.UseMiddleware<SessionDenylistMiddleware>();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapControllers();
app.Run();
public partial class Program;
