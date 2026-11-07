using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Consumers;

/// <summary>
/// Liquida el cobro del run en el Wallet al cerrarse (PEP money-OUT, 00_Plan §5). Consume el evento PROPIO de
/// Campaigns <c>campaign.run.completed.v1</c> y le pide al Wallet consumir lo efectivamente enviado y liberar el
/// resto — así el acoplamiento va Campaigns→Wallet (el Wallet es independiente y NO conoce las campañas).
///
/// <para>Unidades consumidas = destinatarios de canales cobrables (Email/SMS/Push/WhatsApp) que NO quedaron
/// Skipped (opt-out / sin destino): el intento de envío ya ocurrió. El Wallet prorratea por esa fracción. La
/// liquidación del Wallet es idempotente (reserva Open→Settled), así que la re-entrega at-least-once es segura;
/// un run sin reserva (costo 0) resulta no-op. Clase singular (convención Consumer de Wolverine).</para>
/// </summary>
public static class SettleWalletOnRunCompletedConsumer
{
    public static async Task Handle(
        CampaignRunCompletedIntegrationEvent evt,
        ICampaignRunRepository runs,
        IWalletSpendClient wallet,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId!
            )
        )
        {
            // Sin run no hay qué liquidar todavía: descartar perdería el cobro sin dejar rastro.
            var run =
                await runs.GetByIdAsync(evt.TenantId, evt.RunId, ct)
                ?? throw new DependencyNotReadyException("CampaignRun", $"run {evt.RunId}");

            var consumedUnits = run.Recipients.Count(r =>
                BillableChannels.IsBillable(r.Channel) && r.State != DispatchState.Skipped
            );

            // Lanza ante fallo transitorio del Wallet → Wolverine reintenta (la reserva sigue viva). No-op si no
            // había reserva (costo 0).
            await wallet.SettleAsync(evt.TenantId, WalletReferenceTypes.CampaignRun, evt.RunId, consumedUnits, ct);
        }
    }
}
