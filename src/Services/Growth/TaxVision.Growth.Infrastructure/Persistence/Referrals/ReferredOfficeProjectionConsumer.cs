using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Growth.SharedKernel.Buyers;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;
using Wolverine;

namespace TaxVision.Growth.Infrastructure.Persistence.Referrals;

/// <summary>
/// Mantiene el nombre de cada oficina para la lista de referidos (creación, renombre y perfil) y, al crearse, la
/// identidad de su dueño para el antifraude de referidos: solo el HMAC del email normalizado, nunca el email.
/// </summary>
public static class ReferredOfficeProjectionConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ReferredOfficeProjectionStore offices,
        IBuyerIdentityHasher buyerHasher,
        IDisposableEmailDomains disposableDomains,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantCreatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var office = await offices.GetOrCreateAsync(evt.NewTenantId, ct);
        office.AttachOnboarding(evt.OnboardingId);
        office.ApplyName(evt.Name, evt.OccurredOn);
        if (NormalizedEmail.Create(evt.AdminEmail) is { IsSuccess: true } owner)
            office.AttachOwner(buyerHasher.Hash(owner.Value).SubjectId, disposableDomains.IsDisposable(owner.Value));
        await unitOfWork.SaveChangesAsync(ct);

        if (evt.OnboardingId is { } onboardingId)
            await ScreenReferralAsync(bus, onboardingId, evt.CorrelationId, logger, ct);
    }

    // Si el alta ya se enlazó antes de que llegara el email del dueño, la revisión antifraude se completa ahora; si
    // cancela el premio, el comando devuelve el aviso al referidor en su misma transacción.
    private static async Task ScreenReferralAsync(
        IMessageBus bus,
        Guid onboardingId,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var caller = bus.TenantId;
        bus.TenantId = onboardingId.ToString();
        try
        {
            var screened = await bus.InvokeAsync<Result<ReferralScreeningResult>>(
                new ScreenReferralCommand(onboardingId, correlationId),
                ct
            );
            if (screened.IsFailure)
                logger.LogWarning(
                    "Referral of onboarding {OnboardingId} was not screened: {Code}.",
                    onboardingId,
                    screened.Error.Code
                );
        }
        finally
        {
            bus.TenantId = caller;
        }
    }

    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        ReferredOfficeProjectionStore offices,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var office = await offices.GetOrCreateAsync(evt.UpdatedTenantId, ct);
        office.ApplyName(evt.Name, evt.UpdatedAtUtc);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        ReferredOfficeProjectionStore offices,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var office = await offices.GetOrCreateAsync(evt.ProfileTenantId, ct);
        office.ApplyDisplayName(evt.DisplayName, evt.Version);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
