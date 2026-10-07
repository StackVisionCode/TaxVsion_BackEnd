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
            if (topUp is null)
            {
                logger.LogWarning("WalletTopUp {TopUpId} not found for succeeded payment {SaaSPaymentId}.", evt.TopUpId, evt.SaaSPaymentId);
                return;
            }

            var amountMicros = evt.AmountCents * 10_000;

            var wallet = await wallets.GetByTenantAsync(evt.TenantId, ct);
            if (wallet is null)
            {
                var createdWallet = Domain.Wallet.Wallet.Create(evt.TenantId, evt.Currency);
                if (createdWallet.IsFailure)
                {
                    logger.LogError("Could not create wallet for tenant {TenantId}: {Error}", evt.TenantId, createdWallet.Error.Code);
                    return;
                }
                wallet = createdWallet.Value;
                await wallets.AddAsync(wallet, ct);
            }

            var credited = wallet.Credit(amountMicros, opKey: $"topup:{evt.TopUpId:N}", referenceId: evt.TopUpId);
            if (credited.IsFailure)
            {
                logger.LogError("Credit failed for top-up {TopUpId}: {Error}", evt.TopUpId, credited.Error.Code);
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
            await bus.PublishAsync(new WalletBalanceChangedIntegrationEvent { TenantId = evt.TenantId, Reason = "topup" });
        }
    }

    public static async Task Handle(
        WalletTopUpPaymentFailedIntegrationEvent evt,
        IWalletRepository wallets,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            var topUp = await wallets.GetTopUpAsync(evt.TenantId, evt.TopUpId, ct);
            if (topUp is null)
                return;
            topUp.MarkFailed(evt.Reason);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static IDisposable Push(ICorrelationContext correlation, string? correlationId, Guid eventId) =>
        correlation.Push(string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId!);
}
