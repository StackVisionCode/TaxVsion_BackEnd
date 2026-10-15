using System.Globalization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;

/// <summary>
/// Publica el email "completa tu oficina" para los onboardings pagados que NO terminaron in-session
/// dentro de la ventana (RegistrationReminderDelayMinutes). Lo dispara un sweeper
/// (OnboardingRegistrationReminderScheduler). Publish-before-save: el evento y el marcado se
/// comprometen en la MISMA transacción del outbox EF (UseEntityFrameworkCoreTransactions), así que un
/// conflicto de RowVersion revierte ambos → exactamente un email por onboarding.
/// </summary>
public sealed class OnboardingRegistrationReminderProcessor(
    ITenantOnboardingRepository onboardings,
    ITokenReferenceStore tokenReferences,
    IPlanCatalogClient planCatalog,
    IUnitOfWork unitOfWork,
    IMessageBus bus,
    IOptions<OnboardingOptions> onboardingOptions,
    ILogger<OnboardingRegistrationReminderProcessor> logger
)
{
    private const int BatchSize = 50;

    public async Task<int> ProcessDueAsync(DateTime nowUtc, CancellationToken ct)
    {
        var options = onboardingOptions.Value;
        var cutoff = nowUtc.AddMinutes(-options.RegistrationReminderDelayMinutes);
        var due = await onboardings.GetRegistrationRemindersDueAsync(cutoff, BatchSize, ct);
        if (due.Count == 0)
            return 0;

        bus.TenantId = PlatformTenant.Id.ToString();
        var published = 0;
        foreach (var onboarding in due)
        {
            if (onboarding.RegistrationTokenReference is not { } tokenReference)
                continue;

            // La referencia del raw token debe seguir viva para que Notification arme el link. Si venció
            // (Auth caído más que el TTL o Redis vacío), NO marcamos enviado: hacerlo quemaba al cliente
            // en huérfano permanente — pagó pero jamás recibía su link. El sweeper reintenta cada tick (es
            // una query Redis barata) hasta que el admin usa OnboardingAdmin.ResendRegistrationEmail para
            // regenerar un token nuevo. El ERROR log es la señal al operador para intervenir.
            var rawToken = await tokenReferences.PeekAsync(tokenReference, ct);
            if (string.IsNullOrWhiteSpace(rawToken))
            {
                logger.LogError(
                    "Onboarding {OnboardingId} ({Email}) registration reminder blocked: token reference lost. "
                        + "Admin must regenerate via OnboardingAdmin.ResendRegistrationEmail.",
                    onboarding.Id,
                    onboarding.Email
                );
                continue;
            }

            var planName = await planCatalog.GetPlanNameAsync(onboarding.PlanId, ct);
            await bus.PublishAsync(
                new OnboardingRegistrationReadyIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = onboarding.Id,
                    TokenReference = tokenReference,
                    Email = onboarding.Email,
                    FirstName = onboarding.FirstName,
                    PlanName = planName,
                    PriceFormatted = FormatPrice(onboarding.NetAmountCents ?? 0, onboarding.Currency ?? "USD"),
                    PaidAtUtc = onboarding.PaymentCompletedAtUtc ?? nowUtc,
                    RegistrationUrlBase = options.RegistrationUrlBase,
                    CorrelationId = onboarding.Id.ToString("N"),
                }
            );
            onboarding.MarkRegistrationEmailSent(nowUtc);
            published++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return published;
    }

    private static string FormatPrice(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";
}
