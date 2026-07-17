using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Tenants.IntegrationEvents;

public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IRoleRepository roles,
        IMfaRepository mfa,
        ITenantDomainRepository domains,
        ITenantSubdomainReservationRepository reservations,
        IOptions<TenantDomainOptions> domainOptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantCreatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var kind = Enum.TryParse<TenantKind>(evt.Kind, true, out var parsedKind) ? parsedKind : TenantKind.Customer;

            await tenants.UpsertCreatedAsync(evt.NewTenantId, evt.Name, evt.SubDomain, kind, evt.DefaultTimeZoneId, ct);

            if (kind == TenantKind.Customer)
            {
                // Roles de sistema y política MFA por defecto (idempotente).
                await roles.EnsureSystemRolesAsync(evt.NewTenantId, ct);
                if (await mfa.GetPolicyAsync(evt.NewTenantId, ct) is null)
                {
                    await mfa.AddPolicyAsync(Domain.Mfa.TenantMfaPolicy.CreateDefault(evt.NewTenantId), ct);
                }

                await EnsurePrimaryDomainAsync(evt, domains, reservations, domainOptions.Value, logger, ct);

                var existing = await invitations.GetByTokenHashAsync(evt.AdminInvitationTokenHash, ct);
                if (existing is null)
                {
                    var expiresAtUtc = evt.AdminInvitationExpiresAtUtc ?? DateTime.UtcNow.AddDays(7);

                    if (expiresAtUtc <= DateTime.UtcNow)
                    {
                        await unitOfWork.SaveChangesAsync(ct);
                        return;
                    }

                    var invitationResult = Invitation.Create(
                        evt.NewTenantId,
                        evt.AdminEmail,
                        UserActorType.TenantAdmin,
                        customerId: null,
                        invitedByUserId: null,
                        tokenHash: evt.AdminInvitationTokenHash,
                        expiresAtUtc: expiresAtUtc
                    );
                    if (invitationResult.IsFailure)
                        throw new InvalidOperationException(invitationResult.Error.Message);

                    await invitations.AddAsync(invitationResult.Value, ct);
                }
            }

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Crea el TenantDomain primario (subdominio.baseDomain) para el tenant, idempotente.
    /// No lanza si el slug del subdominio no es válido bajo las reglas de Auth (ej. quedó
    /// reservado o cambió de formato) — eso no debe bloquear el alta del resto del tenant
    /// (roles/MFA/invitación); solo se registra para remediación manual (Fase A5/admin).
    /// </summary>
    private static async Task EnsurePrimaryDomainAsync(
        TenantCreatedIntegrationEvent evt,
        ITenantDomainRepository domains,
        ITenantSubdomainReservationRepository reservations,
        TenantDomainOptions options,
        ILogger logger,
        CancellationToken ct
    )
    {
        var existingDomains = await domains.GetByTenantAsync(evt.NewTenantId, ct);
        if (existingDomains.Any(domain => domain.IsPrimary))
            return;

        var slugResult = SubdomainSlug.Create(evt.SubDomain);
        if (slugResult.IsFailure)
        {
            logger.LogWarning(
                "Tenant {TenantId} subdomain {SubDomain} is not a valid TenantDomain slug ({Error}); "
                    + "skipping primary TenantDomain creation. Requires manual remediation.",
                evt.NewTenantId,
                evt.SubDomain,
                slugResult.Error.Code
            );
            return;
        }

        if (await domains.HostExistsAsync($"{slugResult.Value.Value}.{options.BaseDomain}", ct))
        {
            logger.LogWarning(
                "Host for tenant {TenantId} subdomain {SubDomain} already claimed by another TenantDomain row; "
                    + "skipping. Requires manual remediation.",
                evt.NewTenantId,
                evt.SubDomain
            );
            return;
        }

        var domainResult = TenantDomain.CreateSubdomain(
            evt.NewTenantId,
            slugResult.Value,
            options.BaseDomain,
            createdByUserId: Guid.Empty,
            DateTime.UtcNow
        );
        if (domainResult.IsFailure)
        {
            logger.LogWarning(
                "Failed to create primary TenantDomain for tenant {TenantId}: {Error}",
                evt.NewTenantId,
                domainResult.Error.Code
            );
            return;
        }

        await domains.AddAsync(domainResult.Value, ct);

        // Cierra el flujo de §11: si el slug seguía reservado (registro completado
        // dentro del TTL), la reserva se consume — no bloquea el alta si ya expiró o
        // si nunca hubo reserva (tenants creados sin pasar por ReserveSubdomain, ej.
        // el backfill de tenants viejos).
        var nowUtc = DateTime.UtcNow;
        if (await reservations.GetActiveBySlugAsync(slugResult.Value.Value, nowUtc, ct) is { } reservation)
            reservation.Consume(nowUtc);
    }
}
