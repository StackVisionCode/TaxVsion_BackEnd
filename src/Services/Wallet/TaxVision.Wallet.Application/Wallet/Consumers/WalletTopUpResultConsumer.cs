using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Domain.Wallet;
using Wolverine;

namespace TaxVision.Wallet.Application.Wallet.Consumers;

/// <summary>
/// Cierra el flujo de recarga (00_Plan §6): consume los resultados de PaymentApp.
/// <list type="bullet">
/// <item>Succeeded → acredita el saldo (micros), deduplicando por <c>FundingCredit (SourceService, SaaSPaymentId)</c>
/// y por el <c>OperationKey</c> único del ledger — un mismo pago NUNCA acredita dos veces — y marca la orden Credited.
/// Si era automática, devuelve el recibo.</item>
/// <item>Failed → marca la orden Failed (no acredita). Si era automática, pausa la recarga y avisa: no reintenta.</item>
/// </list>
/// Clase singular para que Wolverine la descubra (convención Consumer/Handler).
/// </summary>
public static class WalletTopUpResultConsumer
{
    private const string Source = "PaymentApp";

    /// <summary>Motivo exacto con que PaymentApp falla cuando el método autorizado ya no sirve.</summary>
    public const string MethodUnavailableReason = "MethodUnavailable";

    public static async Task<OutgoingMessages> Handle(
        WalletTopUpPaymentSucceededIntegrationEvent evt,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<WalletTopUp> logger,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            var messages = new OutgoingMessages();
            // Dedupe económico: si ese pago ya se acreditó, no hacer nada (reentrega / clave técnica nueva).
            if (await wallets.FundingCreditExistsAsync(Source, evt.SaaSPaymentId, ct))
                return messages;

            var topUp = await wallets.GetTopUpAsync(evt.TenantId, evt.TopUpId, ct);
            // Dinero cobrado sin orden: nunca se da por procesado (la política lo manda a la DLQ).
            if (topUp is null)
                throw new WalletTopUpNotFoundException(evt.TenantId, evt.TopUpId, evt.SaaSPaymentId);

            // Solo se acredita si la orden es USD y el cobro coincide en moneda y monto; si no, revisión humana.
            var evtCurrency = evt.Currency?.Trim().ToUpperInvariant();
            var amount = topUp.CreditAmount();
            if (
                amount.IsFailure
                || topUp.Currency != Domain.Wallet.Wallet.DefaultCurrency
                || evtCurrency != topUp.Currency
                || evt.AmountCents != topUp.AmountCents
            )
            {
                topUp.MarkNeedsReview($"currency_or_amount_mismatch:{evt.Currency}/{evt.AmountCents}");
                logger.LogError(
                    "Top-up {TopUpId} of tenant {TenantId} not credited: payment {SaaSPaymentId} is {EvtCurrency}/{EvtAmountCents}, order is {OrderCurrency}/{OrderAmountCents}.",
                    evt.TopUpId,
                    evt.TenantId,
                    evt.SaaSPaymentId,
                    evt.Currency,
                    evt.AmountCents,
                    topUp.Currency,
                    topUp.AmountCents
                );
                await unitOfWork.SaveChangesAsync(ct);
                return messages;
            }

            var wallet = await wallets.GetByTenantAsync(evt.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
            if (wallet is null)
            {
                var createdWallet = Domain.Wallet.Wallet.Create(evt.TenantId, Domain.Wallet.Wallet.DefaultCurrency);
                if (createdWallet.IsFailure)
                    throw new InvalidOperationException(
                        $"Could not create wallet for tenant {evt.TenantId}: {createdWallet.Error.Code}"
                    );
                wallet = createdWallet.Value;
                await wallets.AddAsync(wallet, ct);
            }

            var credited = wallet.Credit(
                amount.Value,
                opKey: $"topup:{evt.TopUpId:N}",
                referenceId: evt.TopUpId,
                referenceType: WalletTopUp.ReferenceType
            );
            if (credited.IsFailure)
            {
                topUp.MarkNeedsReview($"credit_rejected:{credited.Error.Code}");
                logger.LogError(
                    "Credit rejected for top-up {TopUpId} of tenant {TenantId}, payment {SaaSPaymentId}: {Error}",
                    evt.TopUpId,
                    evt.TenantId,
                    evt.SaaSPaymentId,
                    credited.Error.Code
                );
                await unitOfWork.SaveChangesAsync(ct);
                return messages;
            }

            await wallets.AddLedgerEntryAsync(credited.Value, ct);
            await wallets.AddFundingCreditAsync(
                FundingCredit.Create(evt.TenantId, Source, evt.SaaSPaymentId, amount.Value.Micros),
                ct
            );
            topUp.MarkCredited(evt.SaaSPaymentId);

            // El aviso de saldo sale del evento de dominio del monedero (BB-1); el recibo, después del commit.
            await unitOfWork.SaveChangesAsync(ct);
            if (topUp.Automatic)
                messages.Add(AutoRechargeNotices.Succeeded(topUp, wallet, evt.CorrelationId));
            return messages;
        }
    }

    public static async Task<OutgoingMessages> Handle(
        WalletTopUpPaymentFailedIntegrationEvent evt,
        IWalletRepository wallets,
        IWalletSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<WalletTopUp> logger,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            var messages = new OutgoingMessages();
            var topUp = await wallets.GetTopUpAsync(evt.TenantId, evt.TopUpId, ct);
            if (topUp is null)
            {
                // Sin dinero de por medio: basta con dejar rastro.
                logger.LogWarning(
                    "WalletTopUp {TopUpId} of tenant {TenantId} not found for failed payment {SaaSPaymentId}.",
                    evt.TopUpId,
                    evt.TenantId,
                    evt.SaaSPaymentId
                );
                return messages;
            }

            var wasPending = topUp.Status == WalletTopUpStatus.Pending;
            topUp.MarkFailed(evt.Reason);
            var settings = topUp.Automatic && wasPending ? await settingsRepository.GetAsync(evt.TenantId, ct) : null;
            var reason =
                evt.Reason == MethodUnavailableReason
                    ? AutoRechargePauseReason.MethodUnavailable
                    : AutoRechargePauseReason.PaymentFailed;
            var pause = settings?.PauseAutoRecharge(reason, DateTime.UtcNow);
            if (pause is not null)
                await settingsRepository.AddChangeAsync(pause, ct);

            await unitOfWork.SaveChangesAsync(ct);
            if (pause is not null)
                messages.Add(AutoRechargeNotices.Paused(topUp, reason, evt.CorrelationId));
            return messages;
        }
    }

    private static IDisposable Push(ICorrelationContext correlation, string? correlationId, Guid eventId) =>
        correlation.Push(string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId!);
}

/// <summary>Avisos de la recarga automática (recibo y pausa).</summary>
internal static class AutoRechargeNotices
{
    private const string FallbackLabel = "Saved payment method";

    public static WalletAutoRechargeSucceededIntegrationEvent Succeeded(
        WalletTopUp topUp,
        Domain.Wallet.Wallet wallet,
        string? correlationId
    ) =>
        new()
        {
            TenantId = topUp.TenantId,
            CorrelationId = correlationId ?? string.Empty,
            TopUpId = topUp.Id,
            AmountCents = topUp.AmountCents,
            Currency = topUp.Currency,
            MethodLabel = topUp.PaymentLabel ?? FallbackLabel,
            AvailableAfterMicros = wallet.AvailableMicros,
        };

    public static WalletAutoRechargePausedIntegrationEvent Paused(
        WalletTopUp topUp,
        AutoRechargePauseReason reason,
        string? correlationId
    ) =>
        new()
        {
            TenantId = topUp.TenantId,
            CorrelationId = correlationId ?? string.Empty,
            TopUpId = topUp.Id,
            Reason = reason.ToString(),
            AmountCents = topUp.AmountCents,
            Currency = topUp.Currency,
            MethodLabel = topUp.PaymentLabel ?? FallbackLabel,
        };
}
