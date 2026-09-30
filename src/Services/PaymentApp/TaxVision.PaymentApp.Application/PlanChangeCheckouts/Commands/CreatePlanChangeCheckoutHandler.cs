using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common.HostedCheckout;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.PlanChangeCheckouts.Commands;

/// <summary>
/// Crea la sesión de checkout hosteada para un upgrade de plan. Los pasos comunes los pone
/// <see cref="HostedCheckoutPipeline"/>; acá solo vive lo propio del cambio de plan. El webhook (source of
/// truth) confirma el pago y publica el MISMO evento que el cobro off-session, que Subscription ya consume
/// para aplicar el plan.
/// </summary>
public static class CreatePlanChangeCheckoutHandler
{
    private const string DefaultStatementDescriptor = "TAXVISION PLAN";

    public static async Task<Result<PlanChangeCheckoutResponse>> Handle(
        CreatePlanChangeCheckoutCommand command,
        ISaaSPaymentRepository payments,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var request = new HostedCheckoutRequest(
            command.IdempotencyKey,
            command.PayerEmail,
            command.SuccessUrl,
            command.CancelUrl,
            command.Provider,
            command.Method
        );

        var result = await HostedCheckoutPipeline.RunAsync(
            request,
            PolicyFor(command),
            payments,
            providerFactory,
            audit,
            unitOfWork,
            metrics,
            correlation,
            logger,
            ct
        );

        return result.IsFailure
            ? Result.Failure<PlanChangeCheckoutResponse>(result.Error)
            : Result.Success(
                new PlanChangeCheckoutResponse(
                    result.Value.PaymentId,
                    result.Value.CheckoutUrl,
                    result.Value.ProviderSessionId,
                    result.Value.ExpiresAtUtc
                )
            );
    }

    private static HostedCheckoutPolicy PolicyFor(CreatePlanChangeCheckoutCommand command) =>
        new()
        {
            PaymentType = SaaSPaymentType.PlanChangeCheckout,
            Subject = "Plan change checkout",
            ReferenceId = command.PlanChangeRequestId,
            StatementDescriptor = DefaultStatementDescriptor,
            // Subscription ya resolvió el precio COMPLETO del plan destino; acá no se prorratea nada.
            ResolveAmount = _ => Task.FromResult(Money.Create(command.AmountCents, command.Currency)),
            CreatePayment = (key, amount, descriptor, nowUtc) =>
                SaaSPayment.Create(
                    command.TenantId,
                    key,
                    amount,
                    SaaSPaymentType.PlanChangeCheckout,
                    command.PlanChangeRequestId,
                    command.Provider,
                    descriptor,
                    actorUserId: Guid.Empty,
                    nowUtc
                ),
            DecideOnExisting = DecideOnExisting,
            BuildMetadata = payment => new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["planChangeRequestId"] = command.PlanChangeRequestId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            BuildAuditPayload = (payment, session) =>
                new
                {
                    payment.Status,
                    PlanChangeRequestId = command.PlanChangeRequestId,
                    session.ProviderSessionId,
                },
        };

    // Clave única por request de upgrade: un re-submit del MISMO request replaya su sesión; un upgrade
    // fallido se reintenta como un request nuevo, con su propia clave.
    private static Result<ExistingPaymentDecision> DecideOnExisting(SaaSPayment existing, DateTime nowUtc) =>
        HostedCheckoutPipeline.TryBuildResponse(existing) is { } replay
            ? Result.Success(ExistingPaymentDecision.Replay(replay))
            : Result.Failure<ExistingPaymentDecision>(
                new Error(
                    "PlanChange.Checkout.NotReplayable",
                    "A checkout already exists for this request but has no usable session."
                )
            );
}
