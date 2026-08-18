using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record CreateTenantFromOnboardingCommand(
    Guid OnboardingId,
    string OfficeName,
    string Subdomain,
    string AdminEmail,
    DateTime PaymentCompletedAtUtc
);

/// <summary>
/// PayFlow (Fase 16) — receptor del <c>POST internal/tenants/from-onboarding</c> que la Saga de Auth
/// (Fase 15, <c>Sagas/Commands/CreateTenantForOnboardingCommand.cs</c>) invoca. No hay invitación de
/// TenantAdmin en este flujo (el owner se crea directo vía Fase 16's
/// <c>internal/tenants/{id}/owners</c>) — <see cref="TenantCreatedIntegrationEvent.AdminInvitationTokenHash"/>
/// queda vacío a propósito: Auth.TenantCreatedConsumer salta ese bloque entero cuando
/// <c>OnboardingId != null</c> (ver Fase 16's cambio a ese consumer), así que el valor nunca se lee.
/// <para>
/// Auditoría F17 — antes este handler validaba "el onboarding está listo" con un M2M síncrono de
/// vuelta a Auth (<c>IAuthOnboardingStatusClient</c>, eliminado). Ahora <see cref="CreateTenantFromOnboardingCommand.PaymentCompletedAtUtc"/>
/// viaja como parte del mismo comando que la Saga ya envía — Auth solo lo puebla cuando el aggregate
/// <c>TenantOnboarding</c> completó el pago (invariante de dominio, ver <c>MarkPaymentCompleted</c>),
/// así que un valor <c>default</c> aquí solo puede venir de un request forjado, y el guard local lo
/// rechaza sin necesidad de otro round-trip.
/// </para>
/// </summary>
public static class CreateTenantFromOnboardingHandler
{
    public static async Task<Result> Handle(
        CreateTenantFromOnboardingCommand command,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await repo.GetByOnboardingIdAsync(command.OnboardingId, ct);
        if (existing is not null)
            return Result.Success();

        if (command.PaymentCompletedAtUtc == default)
            return Result.Failure(
                new Error("Tenant.Onboarding.NotReady", "The onboarding is not ready for tenant provisioning.")
            );

        var adminEmail = command.AdminEmail.Trim().ToLowerInvariant();

        var createResult = Domain.Tenant.Create(
            command.OfficeName,
            command.Subdomain,
            IanaTimeZone.UtcId,
            command.OnboardingId
        );
        if (createResult.IsFailure)
            return Result.Failure(createResult.Error);

        var tenant = createResult.Value;
        await repo.AddAsync(tenant, ct);

        await bus.PublishAsync(
            new TenantCreatedForOnboardingIntegrationEvent
            {
                TenantId = tenant.Id,
                OnboardingId = command.OnboardingId,
                CreatedTenantId = tenant.Id,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await bus.PublishAsync(
            new TenantCreatedIntegrationEvent
            {
                NewTenantId = tenant.Id,
                TenantId = tenant.Id,
                Name = tenant.Name,
                SubDomain = tenant.SubDomain,
                Kind = TenantKind.Customer.ToString(),
                DefaultTimeZoneId = tenant.DefaultTimeZoneId,
                AdminEmail = adminEmail,
                AdminInvitationTokenHash = string.Empty,
                OnboardingId = command.OnboardingId,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
