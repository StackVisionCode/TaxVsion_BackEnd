using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>Fase A5 — instrucciones de verificación DNS que el tenant debe configurar antes de que el hostname active.</summary>
public sealed record TenantDomainCreatedResponse(
    TenantDomainResponse Domain,
    string? OwnershipTxtName,
    string? OwnershipTxtValue,
    IReadOnlyList<string> DcvRecords
);

/// <summary>
/// Fase A5 — alta de un dominio propio (custom hostname) para el tenant. Los
/// subdominios *.taxprocore.com nunca pasan por aquí (wildcard, alta automática en
/// TenantCreatedConsumer, Fase A3) — este comando es solo para el dominio propio
/// futuro del tenant.
/// </summary>
public sealed record CreateTenantDomainCommand(Guid TenantId, Guid CreatedByUserId, string Hostname);

public static class CreateTenantDomainHandler
{
    public static async Task<Result<TenantDomainCreatedResponse>> Handle(
        CreateTenantDomainCommand command,
        ITenantDomainRepository domains,
        ICloudflareProvisioningClient cloudflare,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var domainResult = await CreateDomainAsync(command, domains, ct);
        if (domainResult.IsFailure)
            return Result.Failure<TenantDomainCreatedResponse>(domainResult.Error);

        var domain = domainResult.Value;
        var provisionResult = await ProvisionWithCloudflareAsync(domain, cloudflare, ct);
        if (provisionResult.IsFailure)
        {
            // El dominio nunca se agrega al repositorio en este camino — no queda fila
            // huérfana en Pending. Como el agregado nunca se persiste, no hay entidad
            // rastreada donde colgar un domain event (Fase A7): este es el único caso
            // que sigue auditando/publicando a mano.
            await RecordFailureAsync(domain, provisionResult.Error, audit, request, correlation, bus, unitOfWork, ct);
            return Result.Failure<TenantDomainCreatedResponse>(provisionResult.Error);
        }

        await domains.AddAsync(domain, ct);
        // domain.AddDomainEvent(TenantDomainCreated) ya quedó encolado en el factory;
        // AuthDbContext.SaveChangesAsync lo drena y publica (auditoría + integration event).
        await unitOfWork.SaveChangesAsync(ct);

        var cloudflareResult = provisionResult.Value;
        return Result.Success(
            new TenantDomainCreatedResponse(
                TenantDomainResponse.From(domain),
                cloudflareResult.OwnershipTxtName,
                cloudflareResult.OwnershipTxtValue,
                cloudflareResult.DcvRecords
            )
        );
    }

    private static async Task<Result<TenantDomain>> CreateDomainAsync(
        CreateTenantDomainCommand command,
        ITenantDomainRepository domains,
        CancellationToken ct
    )
    {
        var normalizedHost = command.Hostname?.Trim().ToLowerInvariant() ?? string.Empty;
        if (await domains.HostExistsAsync(normalizedHost, ct))
            return Result.Failure<TenantDomain>(new Error("TenantDomain.HostTaken", "This host is already in use."));

        return TenantDomain.CreateCustomHostname(
            command.TenantId,
            normalizedHost,
            command.CreatedByUserId,
            DateTime.UtcNow
        );
    }

    private static async Task<Result<CustomHostnameResult>> ProvisionWithCloudflareAsync(
        TenantDomain domain,
        ICloudflareProvisioningClient cloudflare,
        CancellationToken ct
    )
    {
        var cloudflareResult = await cloudflare.CreateCustomHostnameAsync(domain.Host, ct);
        if (cloudflareResult.IsFailure)
            return cloudflareResult;

        var markResult = domain.MarkProvisioning(cloudflareResult.Value.CloudflareId, "cname");
        return markResult.IsFailure
            ? Result.Failure<CustomHostnameResult>(markResult.Error)
            : Result.Success(cloudflareResult.Value);
    }

    private static async Task RecordFailureAsync(
        TenantDomain domain,
        Error error,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await audit.AddAsync(
            AuthAuditLog.Record(
                domain.TenantId,
                domain.CreatedByUserId,
                AuthAuditAction.TenantDomainProvisioningFailed,
                false,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "TenantDomain",
                targetId: domain.Id,
                detailsJson: $$"""{"error":"{{error.Code}}"}"""
            ),
            ct
        );

        await bus.PublishAsync(
            new TenantDomainProvisioningFailedIntegrationEvent
            {
                TenantId = domain.TenantId,
                DomainId = domain.Id,
                Host = domain.Host,
                Reason = error.Code,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
    }
}
