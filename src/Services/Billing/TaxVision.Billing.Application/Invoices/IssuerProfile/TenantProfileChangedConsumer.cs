using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using DomainAddress = TaxVision.Billing.Domain.ValueObjects.Address;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Application.Invoices.IssuerProfile;

/// <summary>
/// Proyecta el perfil de la oficina (dueño: Tenant) sobre el <see cref="DomainIssuerProfile"/> de Billing,
/// que es lo que se congela en cada factura. Idempotente por versión: un evento viejo o repetido no
/// cambia nada. El tenant sale de <c>ProfileTenantId</c> (explícito; el repo usa IgnoreQueryFilters).
/// </summary>
public static class TenantProfileChangedConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        IIssuerProfileRepository profiles,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        TimeProvider clock,
        ILogger<DomainIssuerProfile> logger,
        CancellationToken ct
    )
    {
        if (evt.ProfileTenantId == Guid.Empty)
        {
            logger.LogWarning("TenantProfileChanged {EventId} without a tenant; ignoring.", evt.EventId);
            return;
        }

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var profile = await profiles.GetByTenantAsync(evt.ProfileTenantId, ct);
            var isNew = profile is null;
            profile ??= DomainIssuerProfile.Create(evt.ProfileTenantId, nowUtc);

            if (!profile.ApplyTenantProfile(evt.Version, ToOfficeProfile(evt), nowUtc))
            {
                logger.LogInformation(
                    "TenantProfileChanged v{Version} for tenant {TenantId} is not newer than v{Stored}; ignoring.",
                    evt.Version,
                    evt.ProfileTenantId,
                    profile.SourceVersion
                );
                return;
            }

            if (isNew)
                await profiles.AddAsync(profile, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Issuer profile of tenant {TenantId} updated to v{Version}.",
                evt.ProfileTenantId,
                evt.Version
            );
        }
    }

    // La dirección del emisor es la física (la postal no se imprime en la factura).
    private static TenantOfficeProfile ToOfficeProfile(TenantProfileChangedIntegrationEvent evt) =>
        new(
            evt.LegalName,
            evt.DisplayName,
            evt.Ein,
            evt.PhysicalAddress is { } a
                ? DomainAddress.FromParts(a.Line1, a.Line2, a.City, a.State, a.PostalCode, a.Country)
                : null,
            evt.Phone,
            evt.Email,
            evt.Website
        );
}
