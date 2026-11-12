using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans.Events;
using TaxVision.Subscription.Domain.RateLimiting;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.RateLimiting.DomainEvents;

/// <summary>
/// D12: un plan nuevo nace con sus filas de rate limit copiadas del perfil por defecto, explícitas y visibles para el
/// admin. Corre después del commit, desde la cola domain-events. Idempotente: nunca pisa una fila que ya existe.
/// </summary>
public static class PlanCreatedDomainEventHandler
{
    private static readonly CatalogAuditActor Provisioning = CatalogAuditActor.System("rate-limit-provisioning");

    public static async Task Handle(
        PlanCreatedDomainEvent created,
        IRateLimitDefaultProfileRepository profiles,
        IPlanRateLimitRepository rateLimits,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var profile = await profiles.GetAsync(ct);
        var code = PlanCode.Create(created.PlanCode);
        if (profile is null || code.IsFailure)
            return;

        var added = new List<object>();
        foreach (var category in RateLimitDefaultProfile.ScalableCategories)
        {
            if (await rateLimits.GetForUpdateAsync(created.PlanCode, category, ct) is not null)
                continue;

            var multiplier = profile.MultiplierFor(category).Value;
            var row = PlanRateLimit.Seed(Guid.NewGuid(), code.Value, category, multiplier);
            if (row.IsFailure)
                continue;
            await rateLimits.AddAsync(row.Value, ct);
            added.Add(new { Category = category.ToString(), Multiplier = multiplier });
        }

        if (added.Count == 0)
            return;

        await CatalogAudit.RecordAsync(
            audit,
            Provisioning,
            "rate_limit.provisioned",
            new CatalogAuditTarget("PlanRateLimit", created.PlanId, created.PlanCode),
            before: null,
            new { Source = "default-profile", Rows = added },
            reason: null,
            correlation.CorrelationId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}
