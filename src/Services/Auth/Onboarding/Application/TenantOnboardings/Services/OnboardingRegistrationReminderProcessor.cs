using System.Globalization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Onboarding.Application.TenantOnboardings.Services;

/// <summary>
/// El email "completa tu oficina" para los onboardings pagados que NO terminaron in-session dentro de la
/// ventana (RegistrationReminderDelayMinutes). Lo dispara OnboardingRegistrationReminderScheduler en dos pasos:
/// <see cref="PrepareDueAsync"/> lee Redis y el catálogo fuera de la transacción, y
/// <see cref="MarkAndPublishAsync"/> corre dentro de la unidad del job (BB-3), una por onboarding: el marcado y el
/// aviso se confirman juntos. Un conflicto de RowVersion revierte los dos → exactamente un email por onboarding.
/// </summary>
public sealed class OnboardingRegistrationReminderProcessor(
    ITenantOnboardingRepository onboardings,
    ITokenReferenceStore tokenReferences,
    IPlanCatalogClient planCatalog,
    IOptions<OnboardingOptions> onboardingOptions,
    ILogger<OnboardingRegistrationReminderProcessor> logger
)
{
    private const int BatchSize = 50;

    public async Task<IReadOnlyList<OnboardingRegistrationReadyIntegrationEvent>> PrepareDueAsync(
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var options = onboardingOptions.Value;
        var cutoff = nowUtc.AddMinutes(-options.RegistrationReminderDelayMinutes);
        var due = await onboardings.GetRegistrationRemindersDueAsync(cutoff, BatchSize, ct);

        var reminders = new List<OnboardingRegistrationReadyIntegrationEvent>();
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
            reminders.Add(
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
        }

        return reminders;
    }

    /// <summary>Devuelve <c>false</c> si otra réplica ya lo mandó: la unidad no guarda nada.</summary>
    public async Task<bool> MarkAndPublishAsync(
        OnboardingRegistrationReadyIntegrationEvent reminder,
        IMessageBus outbox,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var onboarding = await onboardings.GetByIdAsync(reminder.OnboardingId, ct);
        if (onboarding is null || onboarding.RegistrationEmailSentAtUtc is not null)
            return false;

        onboarding.MarkRegistrationEmailSent(nowUtc);
        outbox.TenantId = PlatformTenant.Id.ToString();
        await outbox.PublishAsync(reminder);
        return true;
    }

    private static string FormatPrice(long amountCents, string currency) =>
        $"{(amountCents / 100m).ToString("F2", CultureInfo.InvariantCulture)} {currency}";
}
