using System.Reflection;
using System.Text.Json.Serialization;
using BuildingBlocks.Infrastructure.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Health;
using BuildingBlocks.Web.Hosting;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Security;
using BuildingBlocks.Web.Session;
using JasperFx.CodeGeneration.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using StackExchange.Redis;
using TaxVision.Growth.Api.Authorization;
using TaxVision.Growth.Api.Common;
using TaxVision.Growth.Api.Messaging;
using TaxVision.Growth.Infrastructure;
using TaxVision.Growth.Infrastructure.Observability;
using TaxVision.Growth.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.RabbitMQ;
using Wolverine.SqlServer;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseTaxVisionSerilog("growth-service");

builder
    .Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddActorTypeAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

builder.Services.AddBuildingBlocks();
builder.Services.AddGrowthInfrastructure(builder.Configuration);

// Red de seguridad: barre reservas de código vencidas (checkout abandonado) → Expired + libera el hold,
// para que un código de un solo uso no quede quemado si nadie llamó Cancel.
builder.Services.AddHostedService<TaxVision.Growth.Infrastructure.Scheduling.ReservationExpirySweeper>();
builder.Services.AddHostedService<TaxVision.Growth.Infrastructure.Scheduling.ReferralRewardGrantWorker>();
builder.Services.AddRedisCache(builder.Configuration);
builder.Services.AddSessionDenylist(builder.Configuration);
builder.Services.AddTaxVisionJwtAuthentication(builder.Configuration);
builder.Services.AddTaxVisionOpenTelemetry(builder.Configuration, "growth-service", GrowthMetrics.MeterName);

builder.Services.Configure<AuthorizationOptions>(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// GrowthAuthorizationPolicyProvider sigue siendo el único IAuthorizationPolicyProvider de Growth
// (ASP.NET Core solo permite uno por app) — ahora compone DOS mecanismos independientes en vez de
// reimplementar permisos a mano: "perm:" delega a PermissionPolicyProvider de BuildingBlocks
// (RBAC Fase 8 — mismo IUserPermissionsSource que los otros 13 microservicios, ver abajo), y
// "service-scope:" sigue siendo el mecanismo propio de Growth para M2M (Audience+scope, Variante B
// de Actor_Type_Authorization_Layers_Plan.md Fase 4) sin relación con permisos de usuario. Se le
// agrega la Capa 2 (ActorTypeAuthorizationFilter, vía .AddActorTypeAuthorization() arriba) ENCIMA,
// sin tocar este provider.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, GrowthAuthorizationPolicyProvider>();

// H-05 — fuente de permisos de la Capa 2. Revienta al arrancar si hay endpoints con
// [HasPermission] y la config no pide "Projection": el claim `perm` ya no se emite (Fase
// 7.5.10), así que en modo Jwt esos endpoints darían 403 siempre, en silencio.
builder.Services.AddUserPermissionsSource(builder.Configuration, Assembly.GetExecutingAssembly());

// Rate limiting propio de Growth (B-02): el Gateway solo limita /auth/* y /storage/*, así que
// /growth/* y los endpoints M2M /internal/* quedaban sin tope. Toda la superficie de Growth
// (incluido Quote/ReserveBenefitGift, ex-limiter nativo "growth-code-quote") vive hoy en el
// sistema tiered ([RateLimit], ver RateLimitPolicyCatalog growth.*) — auditoría independiente
// post-Fase 9 cerró el último gap M2M (la premisa "JWT de servicio sin user_id" era falsa, el
// JWT siempre trae TenantId). Growth ya no registra ningún limiter nativo ASP.NET Core.

// Rate limiting tiered por tenant/usuario (Fase 4.15 del plan). IConnectionMultiplexer/
// IRateCounter no estaban registrados por ninguna fase previa de Growth (a diferencia de
// PaymentApp/Auth), así que se agregan acá — mismo patrón que Tenant/Billing/Correspondence/
// Notification/Customer/CloudStorage/Subscription/Scribe/Signature/PaymentClient.
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(
        builder.Configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.")
    )
);
builder.Services.AddSingleton<IRateCounter, RedisRateCounter>();

// Auditoria RateLimit hallazgo #2 — Growth ganó un acquirer de token M2M saliente (ver
// GrowthInfrastructure.DependencyInjection.AddRateLimitTierQuotas), así que ahora también
// registra IPlanRateLimitReader — la cuota escala por plan en vez de caer siempre a
// NullPlanRateLimitReader.
if (builder.Configuration.GetValue<bool>("RateLimit:EnforceTierQuotas"))
{
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.ITenantPlanCodeReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedTenantPlanCodeReader
    >();
    builder.Services.AddSingleton<
        BuildingBlocks.RateLimiting.IPlanRateLimitReader,
        BuildingBlocks.Infrastructure.RateLimiting.ScopedPlanRateLimitReader
    >();
}

builder.Services.AddTieredRateLimiting();

var rabbitUri = new Uri(
    builder.Configuration["RabbitMq:Uri"] ?? throw new InvalidOperationException("RabbitMq:Uri is missing.")
);

builder
    .Services.AddHealthChecks()
    .AddDbContextCheck<GrowthDbContext>("sql-server", tags: ["ready"])
    .AddCheck("rabbitmq", new TcpEndpointHealthCheck(rabbitUri.Host, rabbitUri.Port), tags: ["ready"]);

builder.Host.UseWolverine(options =>
{
    options.Discovery.IncludeAssembly(Assembly.GetExecutingAssembly());
    options.Discovery.IncludeAssembly(Assembly.Load("TaxVision.Codes.Application"));
    options.Discovery.IncludeAssembly(Assembly.Load("TaxVision.Referrals.Application"));
    // RBAC Fase 7/8 — UserRolesChangedPermissionsProjectionConsumer y
    // RolePermissionsChangedPermissionsProjectionConsumer viven en Growth.Infrastructure (no hay
    // un "Growth.Application" genérico — ver PermissionsProjectionConsumers.cs), así que este
    // assembly también necesita registrarse explícitamente para que Wolverine los descubra.
    options.Discovery.IncludeAssembly(typeof(TaxVision.Growth.Infrastructure.DependencyInjection).Assembly);
    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
    options.Policies.ForMessagesOfType<IIntegrationEvent>().AddMiddleware(typeof(GrowthTenantMessageMiddleware));
    // Global (no message-type filter): restores tenant context for LOCAL commands invoked via
    // bus.InvokeAsync from API controllers. See GrowthLocalCommandTenantMiddleware for why this
    // is needed in addition to the IIntegrationEvent-scoped middleware above.
    options.Policies.AddMiddleware(typeof(GrowthLocalCommandTenantMiddleware));

    var sqlConnection =
        builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

    options.UseTaxVisionRabbitMq(rabbitUri);
    options.PersistMessagesWithSqlServer(sqlConnection, GrowthSchemas.Integration);
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
    options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, GrowthDbContext>();
    options.Policies.AutoApplyTransactions();

    // Primer evento que Growth publica. Hoy no tiene consumidor.
    options.PublishMessage<CodeReservationCommittedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Solicitud de crédito/reversa del premio al Wallet (el Wallet responde con wallet.credit.applied.v1): sin M2M.
    options.PublishMessage<BuildingBlocks.Messaging.WalletIntegrationEvents.WalletCreditRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Aviso al referidor cuando alguien se registra con su código (Notification lo consume).
    options.PublishMessage<BuildingBlocks.Messaging.GrowthIntegrationEvents.ReferralAttributedIntegrationEvent>().ToRabbitExchange("taxvision-events");
    // Envío de una giftcard por correo: Notification lo consume y despacha el email.
    options.PublishMessage<BuildingBlocks.Messaging.GrowthIntegrationEvents.GiftcardShareRequestedIntegrationEvent>().ToRabbitExchange("taxvision-events");

    options
        .ListenToRabbitQueue(
            "growth-events",
            queue =>
            {
                queue.BindExchange("taxvision-events", string.Empty);
            }
        )
        .UseDurableInbox();

    // Antes de la estándar: gana la primera regla que matchea.
    options.ApplyGrowthFailurePolicies();
    options.UseTaxVisionConflictPolicy();
    options.ApplyStandardFailurePolicies();

    // BB-1: eventos de dominio de CodeReservation; en serie para que los avisos salgan en el orden del commit.
    options.UseTaxVisionDomainEvents();
    options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
});

builder.Services.AddTaxVisionClientIpForwarding(builder.Configuration);

var app = builder.Build();

// IP real del cliente detras de Cloudflare/Caddy/Gateway (compartido) — primer middleware.
app.UseTaxVisionClientIp();

// Giftcards de DEMO (WELCOME100, GIFTCARD5…): solo en desarrollo/demo. En producción las giftcards las crea la
// compañía (PlatformAdmin) desde la administración; sembrar códigos de regalo públicos con montos reales sería un
// agujero. Se activa con Growth:Seeding:OnboardingDemoCodes=true (lo pone el overlay local de docker).
if (builder.Configuration.GetValue("Growth:Seeding:OnboardingDemoCodes", false))
await using (var seedScope = app.Services.CreateAsyncScope())
{
    var sp = seedScope.ServiceProvider;
    await TaxVision.Codes.Application.Definitions.Seeding.PlatformOnboardingCodeSeeder.SeedAsync(
        sp.GetRequiredService<TaxVision.Codes.Application.Abstractions.ICodeDefinitionRepository>(),
        sp.GetRequiredService<TaxVision.Codes.Application.Abstractions.ICodeTokenHasher>(),
        sp.GetRequiredService<BuildingBlocks.Persistence.IUnitOfWork>(),
        sp.GetRequiredService<ILogger<Program>>(),
        CancellationToken.None
    );
}

// Referidos — siembra el programa de plataforma por defecto (idempotente por ProgramCode; nunca pisa lo
// que el PlatformAdmin ajustó). Scope propio: fija el tenant de plataforma sin contaminar requests.
await using (var programSeedScope = app.Services.CreateAsyncScope())
{
    var sp = programSeedScope.ServiceProvider;
    await TaxVision.Referrals.Application.Programs.Seeding.DefaultReferralProgramSeeder.SeedAsync(
        sp.GetRequiredService<TaxVision.Referrals.Application.Abstractions.IReferralProgramRepository>(),
        sp.GetRequiredService<BuildingBlocks.Persistence.IUnitOfWork>(),
        sp.GetRequiredService<BuildingBlocks.Tenancy.ITenantContext>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ILogger<Program>>(),
        CancellationToken.None
    );
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Growth API v1"));
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseMiddleware<BuildingBlocks.Web.Session.SessionDenylistMiddleware>();
app.UseMiddleware<JwtTenantContextMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program { }
