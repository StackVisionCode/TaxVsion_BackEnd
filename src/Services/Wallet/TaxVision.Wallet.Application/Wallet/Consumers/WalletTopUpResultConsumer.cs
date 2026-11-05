using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Wallet;
using Wolverine;

namespace TaxVision.Wallet.Application.Wallet.Consumers;

/// <summary>
/// Cierra el flujo de recarga (00_Plan §6): consume los resultados de PaymentApp.
/// <list type="bullet">
/// <item>Succeeded → acredita el saldo (micros), deduplicando por <c>FundingCredit (SourceService, SaaSPaymentId)</c>
/// y por el <c>OperationKey</c> único del ledger — un mismo pago NUNCA acredita dos veces — y marca la orden Credited.</item>
/// <item>Failed → marca la orden Failed (no acredita).</item>
/// </list>
/// Clase singular para que Wolverine la descubra (convención Consumer/Handler).
/// </summary>
public static class WalletTopUpResultConsumer
{
    private const string Source = "PaymentApp";

    public static async Task Handle(
        WalletTopUpPaymentSucceededIntegrationEvent evt,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<WalletTopUp> logger,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            // Dedupe económico: si ese pago ya se acreditó, no hacer nada (reentrega / clave técnica nueva).
            if (await wallets.FundingCreditExistsAsync(Source, evt.SaaSPaymentId, ct))
                return;

            var topUp = await wallets.GetTopUpAsync(evt.TenantId, evt.TopUpId, ct);
            // Dinero cobrado sin orden: nunca se da por procesado (la política lo manda a la DLQ).
            if (topUp is null)
                throw new WalletTopUpNotFoundException(evt.TenantId, evt.TopUpId, evt.SaaSPaymentId);

            // Solo se acredita si la orden es USD y el cobro coincide en moneda y monto; si no, revisión humana.
            var evtCurrency = evt.Currency?.Trim().ToUpperInvariant();
            if (
                topUp.Currency != Domain.Wallet.Wallet.DefaultCurrency
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
                return;
            }

            var amountMicros = topUp.AmountMicros;

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

            var credited = wallet.Credit(amountMicros, opKey: $"topup:{evt.TopUpId:N}", referenceId: evt.TopUpId);
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
                return;
            }

            await wallets.AddLedgerEntryAsync(credited.Value, ct);
            await wallets.AddFundingCreditAsync(
                FundingCredit.Create(evt.TenantId, Source, evt.SaaSPaymentId, amountMicros),
                ct
            );
            topUp.MarkCredited(evt.SaaSPaymentId);

            await unitOfWork.SaveChangesAsync(ct);

            // Tiempo real: la recarga acreditó → avisa para que el front refresque saldo/historial.
            await bus.PublishAsync(
                new WalletBalanceChangedIntegrationEvent
                {
                    TenantId = evt.TenantId,
                    Reason = "topup",
                    CorrelationId = correlation.CorrelationId,
                }
            );
        }
    }

    public static async Task Handle(
        WalletTopUpPaymentFailedIntegrationEvent evt,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<WalletTopUp> logger,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
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
                return;
            }
            topUp.MarkFailed(evt.Reason);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static IDisposable Push(ICorrelationContext correlation, string? correlationId, Guid eventId) =>
        correlation.Push(string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId!);
}
