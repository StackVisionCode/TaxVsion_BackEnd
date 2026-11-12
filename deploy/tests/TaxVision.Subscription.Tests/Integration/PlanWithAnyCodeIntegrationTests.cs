using BuildingBlocks.Results;
using BuildingBlocks.Web.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Plans.Commands.CreatePlan;
using TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;
using TaxVision.Subscription.Application.Plans.Commands.ReplaceDraftVersion;
using TaxVision.Subscription.Application.RateLimiting.DomainEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using Wolverine;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Plan único D8 contra SQL real (base temporal de las T-INT): un plan con un código que no es starter/pro/enterprise
/// se crea, se completa y se publica con los handlers reales; nace con sus rate limits y su edición publicada trae
/// todas las claves de plataforma (archivos incluidos). Nada mira el nombre del plan.
/// </summary>
public sealed class PlanWithAnyCodeIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("anyplan");
    private readonly string _code = $"gold{Guid.NewGuid():N}"[..12];
    private IHost _host = null!;

    public async Task InitializeAsync() =>
        _host = await SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                options.Discovery.IncludeType(typeof(CreatePlanHandler));
                options.Discovery.IncludeType(typeof(ReplaceDraftVersionHandler));
                options.Discovery.IncludeType(typeof(PublishDraftVersionHandler));
                options.Discovery.IncludeType(typeof(PlanCreatedDomainEventHandler));
                options.UseTaxVisionDomainEvents();
                options.UseTaxVisionDurableLocalQueues();
                options.Services.AddScoped<ICatalogAuditLog, CatalogAuditLog>();
                options.Services.AddScoped<IRateLimitDefaultProfileRepository, RateLimitDefaultProfileRepository>();
                options.Services.AddScoped<IPlanRateLimitRepository, PlanRateLimitRepository>();
            }
        );

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_plan_with_any_code_gets_its_rate_limits_and_every_platform_key()
    {
        var bus = _host.Services.GetRequiredService<IMessageBus>();
        var actor = Guid.NewGuid();

        var created = await bus.InvokeAsync<Result<CreatePlanResult>>(
            new CreatePlanCommand(_code, "Gold", "Gold plan", "Pro", ["Monthly"], 14, actor)
        );
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);

        var replaced = await bus.InvokeAsync<Result>(
            new ReplaceDraftVersionCommand(
                created.Value.PlanId,
                created.Value.VersionId,
                ["meetings"],
                [],
                [new DraftEntitlementInput("seats.max", "Int", "4", "seats.max")],
                [new DraftPriceTierInput("Monthly", 1, null, 79m, "USD")],
                14,
                ["Monthly"],
                actor
            )
        );
        Assert.True(replaced.IsSuccess, replaced.IsFailure ? replaced.Error.Code : null);

        var published = await bus.InvokeAsync<Result>(
            new PublishDraftVersionCommand(created.Value.PlanId, created.Value.VersionId, actor)
        );
        Assert.True(published.IsSuccess, published.IsFailure ? published.Error.Code : null);

        await using (var db = SubscriptionIntegrationDb.NewContext(Guid.Empty))
        {
            var plan = await new PlanRepository(db).GetByIdAsync(created.Value.PlanId);
            var edition = plan!.GetPublishedVersion()!;
            Assert.Empty(EntitlementKeyCatalog.MissingFrom(edition));
            Assert.Equal("4", edition.Entitlements.Single(e => e.Key.Value == "seats.max").DefaultValue);
            Assert.Contains(edition.Entitlements, e => e.Key.Value == "storage.max_file_bytes");
        }

        // Los rate limits los copia el aviso de plan creado (perfil por defecto, F5): las 10 categorías F–O.
        Assert.Equal(10, await WaitForRateLimitRowsAsync(10));
    }

    private async Task<int> WaitForRateLimitRowsAsync(int expected)
    {
        var code = PlanCode.Create(_code).Value;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var count = 0;
        while (DateTime.UtcNow < deadline)
        {
            await using var db = SubscriptionIntegrationDb.NewContext(Guid.Empty);
            count = await db.PlanRateLimits.AsNoTracking().CountAsync(row => row.PlanCode == code);
            if (count >= expected)
                return count;
            await Task.Delay(250);
        }

        return count;
    }
}
