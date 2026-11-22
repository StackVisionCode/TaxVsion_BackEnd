using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.GrowthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Referrals.Application.Attributions.CreateReferralAttribution;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;
using TaxVision.Referrals.Application.Attributions.LinkReferralRefereeTenant;
using TaxVision.Referrals.Application.Attributions.OnboardingReferralRejections;
using TaxVision.Referrals.Application.Codes.AutoIssueTenantReferralCode;
using TaxVision.Referrals.Application.Fraud.ScreenReferral;
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
        var program = await bus.InvokeAsync<Result<ReferralProgramTermsResult>>(
            new GetActiveReferralProgramQuery(),
            ct
        );
        if (program.IsFailure)
        {
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} dropped: {Error}.",
                message.OnboardingId,
                program.Error.Code
            );
            await RecordRejectionAsync(bus, message.OnboardingId, program.Error.Code, logger, ct);
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
                        message.ShareId,
                        // El aviso al referidor lo devuelve el comando y sale en su misma transacción.
                        NoticeCorrelationId: message.CorrelationId
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
            await RecordRejectionAsync(bus, message.OnboardingId, attributed.Error.Code, logger, ct);
        }
    }

    // El pago del alta lo consulta para no reintentar un referido que nunca tendrá atribución.
    private static async Task RecordRejectionAsync(
        IMessageBus bus,
        Guid onboardingId,
        string reasonCode,
        ILogger logger,
        CancellationToken ct
    )
    {
        var recorded = await OnboardingReferralTenant.InAsync(
            bus,
            onboardingId,
            () => bus.InvokeAsync<Result>(new RecordOnboardingReferralRejectionCommand(onboardingId, reasonCode), ct)
        );
        if (recorded.IsFailure)
            logger.LogWarning(
                "Rejection of the referral of onboarding {OnboardingId} was not recorded: {Code}.",
                onboardingId,
                recorded.Error.Code
            );
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
        if (attribution.IsFailure)
            return;

        if (attribution.Value is not { } attributionId)
        {
            await EnsureNoReferralPendingAsync(message, bus, logger, ct);
            return; // el comprador no llegó por referido, o su código se descartó
        }

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
                        SystemActor,
                        // El aviso de premio en espera lo devuelve el comando y sale en su misma transacción.
                        message.CorrelationId
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

    // Si el comprador declaró un código y la atribución no está, la declaración puede seguir en camino: se reintenta
    // (BB-7) salvo que Growth ya la haya descartado. Sin la marca (eventos viejos) o sin código, no hay nada que esperar.
    private static async Task EnsureNoReferralPendingAsync(
        OnboardingPaymentSucceededIntegrationEvent message,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (message.ReferralDeclared != true)
            return;

        var rejected = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () => bus.InvokeAsync<Result<bool>>(new HasOnboardingReferralRejectionQuery(message.OnboardingId), ct)
        );
        if (rejected.IsSuccess && rejected.Value)
        {
            logger.LogInformation(
                "Referral of onboarding {OnboardingId} was rejected; the paid signup has nothing to qualify.",
                message.OnboardingId
            );
            return;
        }

        throw new DependencyNotReadyException(
            "ReferralAttribution",
            $"onboarding {message.OnboardingId} declared a referral code that is not attributed yet"
        );
    }
}

/// <summary>
/// El alta se completó y creó su oficina: su atribución de referido (si la hay) queda enlazada con esa oficina real y con
/// el usuario que la completó, y se revisa contra el referidor (antifraude). Idempotente. Otra oficina ya enlazada no se
/// reemplaza: se registra y el evento termina.
/// </summary>
public static class ReferralRefereeTenantLinkConsumer
{
    public static async Task Handle(
        TenantOnboardingCompletedIntegrationEvent message,
        IMessageBus bus,
        ILogger<TenantOnboardingCompletedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var linked = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () =>
                bus.InvokeAsync<Result>(
                    new LinkReferralRefereeTenantCommand(
                        message.OnboardingId,
                        message.CompletedTenantId,
                        message.CompletedUserId
                    ),
                    ct
                )
        );

        if (linked.IsFailure)
        {
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} was not linked to office {TenantId}: {Code}.",
                message.OnboardingId,
                message.CompletedTenantId,
                linked.Error.Code
            );
            return;
        }

        // Autorreferido bloqueado (G8): el aviso de premio cancelado lo devuelve el comando, en su misma transacción.
        var screened = await OnboardingReferralTenant.InAsync(
            bus,
            message.OnboardingId,
            () =>
                bus.InvokeAsync<Result<ReferralScreeningResult>>(
                    new ScreenReferralCommand(message.OnboardingId, message.CorrelationId),
                    ct
                )
        );
        if (screened.IsFailure)
        {
            logger.LogWarning(
                "Referral of onboarding {OnboardingId} was not screened: {Code}.",
                message.OnboardingId,
                screened.Error.Code
            );
        }
    }
}

/// <summary>
/// La oficina nueva recibe su código de referido sin pulsar "Regenerate" (corre con el tenant de la oficina). Un replay
/// no crea otro y, sin programa activo, no se emite nada.
/// </summary>
public static class ReferralCodeAutoIssueConsumer
{
    public static async Task Handle(
        TenantOnboardingCompletedIntegrationEvent message,
        IMessageBus bus,
        ILogger<TenantOnboardingCompletedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var issued = await OnboardingReferralTenant.InAsync(
            bus,
            message.CompletedTenantId,
            () =>
                bus.InvokeAsync<Result<AutoIssueTenantReferralCodeOutcome>>(
                    new AutoIssueTenantReferralCodeCommand(
                        message.CompletedTenantId,
                        message.CompletedUserId,
                        message.OccurredOn
                    ),
                    ct
                )
        );

        if (issued.IsFailure)
            throw GrowthErrorClassification.ToException(issued.Error);

        logger.LogInformation(
            "Referral code for office {TenantId}: {Outcome}.",
            message.CompletedTenantId,
            issued.Value
        );
    }
}
