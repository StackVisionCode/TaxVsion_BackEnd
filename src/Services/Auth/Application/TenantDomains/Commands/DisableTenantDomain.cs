using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A5 — deshabilita un dominio propio del tenant. Si es un custom hostname,
/// primero se borra en Cloudflare (anti subdomain-takeover, ver
/// Config/cloudflare-prod.md §3) y solo si eso funciona se deshabilita en Auth — un
/// registro huérfano en Cloudflare es el riesgo real; uno huérfano en nuestra BD no.
/// </summary>
public sealed record DisableTenantDomainCommand(Guid TenantId, Guid DomainId, Guid ActingUserId);

public static class DisableTenantDomainHandler
{
    public static async Task<Result> Handle(
        DisableTenantDomainCommand command,
        ITenantDomainRepository domains,
        ICloudflareProvisioningClient cloudflare,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var domainResult = await TenantDomainAccessGuard.LoadOwnedAsync(
            domains,
            command.TenantId,
            command.DomainId,
            ct
        );
        if (domainResult.IsFailure)
            return domainResult;

        var domain = domainResult.Value;
        var deprovisionResult = await DeprovisionFromCloudflareAsync(domain, cloudflare, ct);
        if (deprovisionResult.IsFailure)
            return deprovisionResult;

        // domain.Disable encola TenantDomainDisabled (domain event); la auditoría y el
        // TenantDomainDisabledIntegrationEvent salen de ahí, vía
        // AuthDbContext.SaveChangesAsync — ver TenantDomainDisabledHandler (Fase A7).
        var disableResult = domain.Disable(command.ActingUserId);
        if (disableResult.IsFailure)
            return disableResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Task<Result> DeprovisionFromCloudflareAsync(
        TenantDomain domain,
        ICloudflareProvisioningClient cloudflare,
        CancellationToken ct
    ) =>
        domain.CloudflareCustomHostnameId is { } cloudflareId
            ? cloudflare.DeleteCustomHostnameAsync(cloudflareId, ct)
            : Task.FromResult(Result.Success());
}
