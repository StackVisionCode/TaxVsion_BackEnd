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
    string AdminEmail
);

/// <summary>
/// PayFlow (Fase 16) — receptor del <c>POST tenants/internal/from-onboarding</c> que la Saga de Auth
/// (Fase 15, <c>Sagas/Commands/CreateTenantForOnboardingCommand.cs</c>) invoca vía
/// <see cref="IAuthOnboardingStatusClient"/> como paso previo. No hay invitación de TenantAdmin en
/// este flujo (el owner se crea directo vía Fase 16's <c>auth/internal/tenants/{id}/owners</c>) —
/// <see cref="TenantCreatedIntegrationEvent.AdminInvitationTokenHash"/> queda vacío a propósito:
/// Auth.TenantCreatedConsumer salta ese bloque entero cuando <c>OnboardingId != null</c> (ver
/// Fase 16's cambio a ese consumer), así que el valor nunca se lee.
/// </summary>
public static class CreateTenantFromOnboardingHandler
{
    public static async Task<Result> Handle(
        CreateTenantFromOnboardingCommand command,
        ITenantRepository repo,
        IAuthOnboardingStatusClient onboardingStatus,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await repo.GetByOnboardingIdAsync(command.OnboardingId, ct);
        if (existing is not null)
            return Result.Success();

        var statusResult = await onboardingStatus.GetStatusAsync(command.OnboardingId, ct);
        if (statusResult.IsFailure)
            return Result.Failure(statusResult.Error);

        if (statusResult.Value.Status != "Provisioning" || statusResult.Value.PaymentCompletedAtUtc is null)
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
