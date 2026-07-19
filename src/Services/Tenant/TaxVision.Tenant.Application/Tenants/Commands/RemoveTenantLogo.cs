using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record RemoveTenantLogoCommand(Guid TenantId);

/// <summary>Idempotente (§5 del plan): sin logo hoy, es un no-op exitoso — igual que un DELETE HTTP idempotente.</summary>
public static class RemoveTenantLogoHandler
{
    public static async Task<Result> Handle(
        RemoveTenantLogoCommand cmd,
        ITenantRepository repo,
        ITenantBrandingCloudStorageClient client,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var tenant = await repo.GetByIdAsync(cmd.TenantId, ct);
        if (tenant is null)
            return Result.Failure(new Error("Tenant.NotFound", "Tenant not found."));

        if (tenant.LogoFileId is not { } fileId)
            return Result.Success();

        var deleteResult = await client.DeleteAsync(cmd.TenantId, fileId, ct);
        if (deleteResult.IsFailure)
            return deleteResult;

        tenant.RemoveLogo();
        await unitOfWork.SaveChangesAsync(ct);

        var removedAtUtc = DateTime.UtcNow;
        await bus.PublishAsync(
            new TenantLogoRemovedIntegrationEvent
            {
                TenantId = cmd.TenantId,
                RemovedAtUtc = removedAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success();
    }
}
