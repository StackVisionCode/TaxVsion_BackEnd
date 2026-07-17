using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A5 — confirma en Cloudflare que el custom hostname ya está en
/// status=active &amp; ssl.status=active y, solo entonces, lo pasa a Active en Auth.
/// Si Cloudflare todavía no lo reporta listo, falla sin mutar nada — el tenant admin
/// debe terminar de configurar sus registros DNS primero.
/// </summary>
public sealed record ActivateTenantDomainCommand(Guid TenantId, Guid DomainId, Guid ActingUserId);

public static class ActivateTenantDomainHandler
{
    public static async Task<Result<TenantDomainResponse>> Handle(
        ActivateTenantDomainCommand command,
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
            return Result.Failure<TenantDomainResponse>(domainResult.Error);

        var domain = domainResult.Value;
        var readinessResult = await ConfirmReadyInCloudflareAsync(domain, cloudflare, ct);
        if (readinessResult.IsFailure)
            return Result.Failure<TenantDomainResponse>(readinessResult.Error);

        // domain.MarkActive encola TenantDomainActivated (domain event); la auditoría y
        // los TenantDomainVerified/ActivatedIntegrationEvent salen de ahí, vía
        // AuthDbContext.SaveChangesAsync — ver TenantDomainActivatedHandler (Fase A7).
        // El mismo evento cubre tanto esta activación manual como la automática del poller.
        var activateResult = domain.MarkActive(DateTime.UtcNow, command.ActingUserId);
        if (activateResult.IsFailure)
            return Result.Failure<TenantDomainResponse>(activateResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(TenantDomainResponse.From(domain));
    }

    private static async Task<Result> ConfirmReadyInCloudflareAsync(
        TenantDomain domain,
        ICloudflareProvisioningClient cloudflare,
        CancellationToken ct
    )
    {
        if (domain.CloudflareCustomHostnameId is not { } cloudflareId)
            return Result.Failure(
                new Error("TenantDomain.NotCustomHostname", "Only custom hostnames go through provisioning.")
            );

        var statusResult = await cloudflare.GetCustomHostnameAsync(cloudflareId, ct);
        if (statusResult.IsFailure)
            return Result.Failure(statusResult.Error);

        return statusResult.Value.IsFullyActive
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "TenantDomain.NotReadyForActivation",
                    "Cloudflare has not confirmed DNS/TLS validation for this hostname yet."
                )
            );
    }
}
