using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Application.TenantPayments.Common;
using TaxVision.PaymentClient.Domain.TenantPayments;
using Wolverine;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;

/// <summary>
/// Resuelve un pago que quedó esperando al proveedor sin webhook (oficina sin webhook secret, webhook
/// perdido, 3DS abandonado). Le pregunta el estado real con las credenciales de la oficina y lo aplica
/// por <see cref="TenantPaymentProviderUpdate"/>, el mismo camino del webhook. Nunca inventa un fallo:
/// si el proveedor sigue sin resolverlo, el pago queda como está. Cada consulta (resuelva o no) se anota
/// en el pago para que la revisión rote y no se atasque siempre en los mismos.
/// </summary>
public static class ReconcileTenantPaymentHandler
{
    public static async Task<Result<TenantPaymentReconciliation>> Handle(
        ReconcileTenantPaymentCommand command,
        ITenantPaymentRepository payments,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        IPaymentAdapterFactory providerFactory,
        ISecretProtector secretProtector,
        IOptions<PlatformStripeCredentials> platformCredentials,
        IPaymentLinkRepository links,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentClientMetrics metrics,
        ICorrelationContext correlation,
        ILogger<TenantPayment> logger,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.TenantPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure<TenantPaymentReconciliation>(
                new Error("TenantPayment.NotFound", "TenantPayment does not exist.")
            );

        if (!payment.IsAwaitingProviderConfirmation)
            return Result.Success(TenantPaymentReconciliation.AlreadyResolved);

        var nowUtc = DateTime.UtcNow;
        payment.RecordProviderCheck(nowUtc);

        var target = await ResolveTargetAsync(
            payment,
            configs,
            connectAccounts,
            secretProtector,
            platformCredentials.Value,
            ct
        );
        if (target.IsFailure)
            return await StampAndFailAsync(target.Error);

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        var statusResult = await adapter.GetChargeStatusAsync(target.Value.Credentials, target.Value.Lookup, ct);
        if (statusResult.IsFailure)
            return await StampAndFailAsync(statusResult.Error);

        var reported = statusResult.Value;
        if (reported.Status is PaymentStatus.Processing or PaymentStatus.RequiresAction or PaymentStatus.Pending)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(TenantPaymentReconciliation.StillPending);
        }

        var statusBefore = payment.Status;
        var applied = TenantPaymentProviderUpdate.Apply(payment, reported, metrics);
        if (applied.IsFailure)
            return await StampAndFailAsync(applied.Error);

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(TenantPayment),
            payment.Id,
            TenantPaymentProviderUpdate.MapAuditAction(payment.Status),
            actorUserId: Guid.Empty,
            correlation.CorrelationId,
            before: new { Status = statusBefore },
            after: new { payment.Status, Source = $"{payment.ProviderCode}Reconciliation" },
            reason: null,
            nowUtc,
            ct
        );

        PaymentLinkCompletionEvents? linkEvents = null;
        if (payment.Status == PaymentStatus.Succeeded)
        {
            metrics.RecordPaymentSucceeded(payment.Amount.AmountCents, payment.Amount.Currency);
            linkEvents = await TenantPaymentProviderUpdate.CompletePaymentLinkIfAnyAsync(
                payment,
                links,
                audit,
                metrics,
                correlation,
                source: "Reconciliation",
                ct
            );
        }

        // Guardar antes de publicar: un reintento en línea no debe filtrar eventos del intento revertido.
        await unitOfWork.SaveChangesAsync(ct);
        await TenantPaymentProviderUpdate.PublishAsync(bus, linkEvents);

        logger.LogInformation(
            "Reconciled TenantPayment {TenantPaymentId} for tenant {TenantId} with {Provider}: {Before} -> {Status}.",
            payment.Id,
            payment.TenantId,
            payment.ProviderCode,
            statusBefore,
            payment.Status
        );
        return Result.Success(TenantPaymentReconciliation.Resolved);

        // La anotación de la consulta se guarda igual: la próxima vuelta reintenta sin acaparar la cola.
        async Task<Result<TenantPaymentReconciliation>> StampAndFailAsync(Error error)
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<TenantPaymentReconciliation>(error);
        }
    }

    private static async Task<Result<ReconciliationTarget>> ResolveTargetAsync(
        TenantPayment payment,
        ITenantPaymentConfigRepository configs,
        ITenantConnectAccountRepository connectAccounts,
        ISecretProtector secretProtector,
        PlatformStripeCredentials platformCredentials,
        CancellationToken ct
    )
    {
        var config = await configs.GetByTenantAndProviderAsync(payment.TenantId, payment.ProviderCode, ct);
        if (config is null || !config.IsActive)
            return Result.Failure<ReconciliationTarget>(
                new Error("TenantPaymentConfig.NotActive", "The provider config is missing or inactive.")
            );

        // Cobro Connect: el cargo vive en la cuenta conectada y se consulta con la clave de la plataforma.
        if (payment.ProviderChargeReferenceOnConnect is { Length: > 0 } connectReference)
        {
            var account = await connectAccounts.GetByTenantAndProviderAsync(payment.TenantId, payment.ProviderCode, ct);
            if (account is null)
                return Result.Failure<ReconciliationTarget>(
                    new Error("Connect.AccountNotFound", "The tenant's Connect account does not exist.")
                );

            return Result.Success(
                new ReconciliationTarget(
                    new TenantProviderCredentials(platformCredentials.PlatformSecretKey, WebhookSecret: null),
                    new ChargeStatusLookup(connectReference, account.StripeConnectAccountId.Value)
                )
            );
        }

        if (payment.ExternalChargeReference is not { } reference || string.IsNullOrWhiteSpace(reference.Value))
            return Result.Failure<ReconciliationTarget>(
                new Error("TenantPayment.NoProviderReference", "The payment has no provider charge reference.")
            );

        if (
            config.SecretKeyEncrypted is null
            || !secretProtector.TryUnprotect(config.SecretKeyEncrypted.CipherText, out var secretKey, out _)
        )
            return Result.Failure<ReconciliationTarget>(
                new Error("TenantPaymentConfig.SecretUnreadable", "Stored provider secret could not be decrypted.")
            );

        return Result.Success(
            new ReconciliationTarget(
                new TenantProviderCredentials(secretKey, null, config.PublishableKey, config.ApiBaseUrl),
                new ChargeStatusLookup(reference.Value)
            )
        );
    }

    private sealed record ReconciliationTarget(TenantProviderCredentials Credentials, ChargeStatusLookup Lookup);
}
