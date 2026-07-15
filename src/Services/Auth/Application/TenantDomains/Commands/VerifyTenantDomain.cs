using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>Fase A5 — snapshot de verificación reportado por Cloudflare, sin mutar el TenantDomain.</summary>
public sealed record TenantDomainVerificationResponse(
    TenantDomainResponse Domain,
    string CloudflareStatus,
    string CloudflareSslStatus,
    string? OwnershipTxtName,
    string? OwnershipTxtValue,
    IReadOnlyList<string> DcvRecords
);

/// <summary>
/// Fase A5 — consulta (sin mutar) el estado de verificación DNS/TLS en Cloudflare
/// para que el tenant admin vea el progreso mientras configura sus registros. La
/// transición real a Active la hace ActivateTenantDomainCommand — o el poller de
/// fondo automáticamente.
/// </summary>
public sealed record RequestTenantDomainVerificationCommand(Guid TenantId, Guid DomainId);

public static class RequestTenantDomainVerificationHandler
{
    public static async Task<Result<TenantDomainVerificationResponse>> Handle(
        RequestTenantDomainVerificationCommand command,
        ITenantDomainRepository domains,
        ICloudflareProvisioningClient cloudflare,
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
            return Result.Failure<TenantDomainVerificationResponse>(domainResult.Error);

        var domain = domainResult.Value;
        if (domain.CloudflareCustomHostnameId is not { } cloudflareId)
            return Result.Failure<TenantDomainVerificationResponse>(
                new Error("TenantDomain.NotCustomHostname", "Only custom hostnames go through provisioning.")
            );

        var statusResult = await cloudflare.GetCustomHostnameAsync(cloudflareId, ct);
        if (statusResult.IsFailure)
            return Result.Failure<TenantDomainVerificationResponse>(statusResult.Error);

        var status = statusResult.Value;
        return Result.Success(
            new TenantDomainVerificationResponse(
                TenantDomainResponse.From(domain),
                status.Status,
                status.SslStatus,
                status.OwnershipTxtName,
                status.OwnershipTxtValue,
                status.DcvRecords
            )
        );
    }
}
