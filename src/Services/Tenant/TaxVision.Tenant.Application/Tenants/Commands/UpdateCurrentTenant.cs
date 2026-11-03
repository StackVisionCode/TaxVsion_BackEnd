using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

/// <summary>Estado completo editable de la oficina: nombre y zona horaria IANA (ambos obligatorios).</summary>
public sealed record UpdateCurrentTenantCommand(Guid TenantId, string Name, string DefaultTimeZoneId);

/// <summary>
/// Renombra la oficina y/o cambia su zona horaria. Si algo cambió, guarda y recién después publica
/// <see cref="TenantUpdatedIntegrationEvent"/> con el estado completo (outbox de Wolverine) para que
/// cada proyección lo copie. Sin cambios no publica: un PUT repetido es idempotente.
/// </summary>
public static class UpdateCurrentTenantHandler
{
    public static async Task<Result<TenantResponse>> Handle(
        UpdateCurrentTenantCommand command,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICacheService cache,
        ICorrelationContext correlation,
        ILogger<UpdateCurrentTenantCommand> logger,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantResponse>(new Error("Tenant.NotFound", "Tenant does not exist."));

        var update = tenant.UpdateNameAndTimeZone(command.Name, command.DefaultTimeZoneId, DateTime.UtcNow);
        if (update.IsFailure)
            return Result.Failure<TenantResponse>(update.Error);

        var response = new TenantResponse(tenant.Id, tenant.Name, tenant.SubDomain, tenant.DefaultTimeZoneId);
        if (!update.Value)
            return Result.Success(response);

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new TenantUpdatedIntegrationEvent
            {
                TenantId = tenant.Id,
                UpdatedTenantId = tenant.Id,
                Name = tenant.Name,
                DefaultTimeZoneId = tenant.DefaultTimeZoneId,
                UpdatedAtUtc = tenant.UpdatedAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        // El directorio M2M (backfill de otros servicios) sale de esta caché: debe ver el nombre nuevo.
        try
        {
            await TenantListCache.InvalidateAsync(cache, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tenant list cache invalidation failed for tenant {TenantId}.", tenant.Id);
        }

        return Result.Success(response);
    }
}
