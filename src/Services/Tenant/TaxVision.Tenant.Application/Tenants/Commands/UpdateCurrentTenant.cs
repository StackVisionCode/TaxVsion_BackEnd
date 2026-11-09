using BuildingBlocks.Caching;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Commands;

/// <summary>Estado completo editable de la oficina: nombre y zona horaria IANA (ambos obligatorios).</summary>
public sealed record UpdateCurrentTenantCommand(Guid TenantId, string Name, string DefaultTimeZoneId);

/// <summary>
/// Renombra la oficina y/o cambia su zona horaria. Si algo cambió, guarda; el agregado levanta el evento de
/// dominio y su handler devuelve <see cref="TenantUpdatedIntegrationEvent"/> con el estado completo para que
/// cada proyección lo copie. Sin cambios no publica: un PUT repetido es idempotente.
/// </summary>
public static class UpdateCurrentTenantHandler
{
    public static async Task<Result<TenantResponse>> Handle(
        UpdateCurrentTenantCommand command,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<UpdateCurrentTenantCommand> logger,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null)
            return Result.Failure<TenantResponse>(new Error("Tenant.NotFound", "Tenant does not exist."));

        var update = tenant.UpdateNameAndTimeZone(
            command.Name,
            command.DefaultTimeZoneId,
            clock.GetUtcNow().UtcDateTime
        );
        if (update.IsFailure)
            return Result.Failure<TenantResponse>(update.Error);

        var response = new TenantResponse(tenant.Id, tenant.Name, tenant.SubDomain, tenant.DefaultTimeZoneId);
        if (!update.Value)
            return Result.Success(response);

        await unitOfWork.SaveChangesAsync(ct);

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
