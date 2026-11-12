using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;
using TaxVision.Referrals.Application.Programs.GetActiveReferralProgram;
using TaxVision.Referrals.Application.Qualifications.QualifyReferral;
using TaxVision.Referrals.Domain.Participants;
using TaxVision.Referrals.Domain.Programs;
using Wolverine;

namespace TaxVision.Growth.Api.IntegrationEvents;

/// <summary>
/// Referidos del alta (pre-tenant), por EVENTO: sin llamadas M2M de Auth a Growth. El <c>OnboardingId</c> hace de referido
/// y de tenant dueño de la atribución, así que los comandos corren con ese tenant en contexto (<c>bus.TenantId</c>).
/// </summary>
internal static class OnboardingReferralTenant
{
    public static async Task<T> InAsync<T>(IMessageBus bus, Guid tenantId, Func<Task<T>> action)
    {
        var caller = bus.TenantId;
        bus.TenantId = tenantId.ToString();
        try
        {
            return await action();
        }
        finally
        {
            bus.TenantId = caller;
        }
    }
}

/// <summary>
/// El comprador declaró un código de referido: se registra la atribución (idempotente por alta) y se avisa al referidor.
/// Un código inválido/revocado o un programa inactivo NO es un error del mensaje: se registra y se descarta (el referido
/// paga igual el precio completo); solo las fallas transitorias relanzan para que Wolverine reintente.
/// </summary>
public static class OnboardingReferralDeclaredConsumer
{
    private static readonly Guid SystemActor = PlatformTenant.Id;

    public static async Task Handle(
        OnboardingReferralDeclaredIntegrationEvent message,
        IMessageBus bus,
        ILogger<OnboardingReferralDeclaredIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var program = await bus.InvokeAsync<Result<ReferralProgramTermsResult>>(new GetActiveReferralProgramQuery(), ct);
        if (program.IsFailure)
        {
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} dropped: {Error}.",
                message.OnboardingId,
                program.Error.Code
            );
            return;
        }

        var attributed = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () =>
                bus.InvokeAsync<Result<CreateReferralAttributionResult>>(
                    new CreateReferralAttributionCommand(
                        message.OnboardingId,
                        program.Value.ProgramId,
                        message.ReferralCode,
                        ReferralParticipantType.Tenant,
                        message.OnboardingId,
                        $"onboarding-referral:{message.OnboardingId:N}",
                        SystemActor,
                        ParseChannel(message.Channel),
                        message.ShareId
                    ),
                    ct
                )
        );

        if (attributed.IsFailure)
        {
            // Las fallas de infraestructura lanzan excepción (Wolverine reintenta); un Result fallido es regla de negocio.
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} was not attributed: {Code}. The signup continues at full price.",
                message.OnboardingId,
                attributed.Error.Code
            );
            return;
        }

        // Aviso al referidor (por el outbox); una reentrega idempotente no avisa dos veces.
        if (!attributed.Value.WasReplay && attributed.Value.ReferrerTenantId != Guid.Empty)
        {
            await bus.PublishAsync(
                new ReferralAttributedIntegrationEvent
                {
                    TenantId = attributed.Value.ReferrerTenantId,
                    AggregateId = attributed.Value.AttributionId,
                    AttributionId = attributed.Value.AttributionId,
                    ReferrerUserId = attributed.Value.ReferrerUserId,
                    Channel = attributed.Value.Channel,
                }
            );
        }
    }

    private static TaxVision.Referrals.Domain.Shares.ReferralShareChannel? ParseChannel(string? channel) =>
        Enum.TryParse<TaxVision.Referrals.Domain.Shares.ReferralShareChannel>(channel, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}

/// <summary>
/// El pago del alta se confirmó: si el comprador llegó con un código de referido, se califica (el premio al referidor
/// nace aquí). Solo con ingreso real (monto &gt; 0). Sin atribución, el evento no tiene nada que hacer. Idempotente por
/// el id del evento. Reemplaza la llamada M2M de Auth «qualifications».
/// </summary>
public static class OnboardingPaymentQualifiesReferralConsumer
{
    private static readonly Guid SystemActor = PlatformTenant.Id;

    public static async Task Handle(
        OnboardingPaymentSucceededIntegrationEvent message,
        IMessageBus bus,
        ILogger<OnboardingPaymentSucceededIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        if (message.AmountPaidCents <= 0)
            return;

        var attribution = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () =>
                bus.InvokeAsync<Result<Guid?>>(
                    new FindAttributionByRefereeQuery(message.OnboardingId, message.OnboardingId),
                    ct
                )
        );
        if (attribution.IsFailure || attribution.Value is not { } attributionId)
            return; // el comprador no llegó por referido

        var qualified = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () =>
                bus.InvokeAsync<Result<QualifyReferralResult>>(
                    new QualifyReferralCommand(
                        message.OnboardingId,
                        attributionId,
                        message.EventId,
                        message.SaaSPaymentId,
                        QualifyingPaymentSource.PaymentApp,
                        message.AmountPaidCents,
                        message.Currency,
                        IsFirstSuccessfulPayment: true,
                        message.PaidAtUtc,
                        $"event:{message.EventId:N}",
                        SystemActor
                    ),
                    ct
                )
        );

        if (qualified.IsFailure)
        {
            // Una calificación rechazada por negocio (ya calificada, fuera de ventana, cupo…) no se reintenta.
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} was not qualified: {Code}.",
                message.OnboardingId,
                qualified.Error.Code
            );
        }
    }
}
