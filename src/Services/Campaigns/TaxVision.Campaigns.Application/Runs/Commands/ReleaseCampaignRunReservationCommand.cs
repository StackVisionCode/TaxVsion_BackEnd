using TaxVision.Campaigns.Application.Runs.Abstractions;
using Wolverine.Attributes;

namespace TaxVision.Campaigns.Application.Runs.Commands;

/// <summary>
/// Libera lo que el Wallet haya apartado para un run rechazado: nada se envió, así que no se consume ninguna unidad.
/// Sin reserva es un no-op. Va por cola durable porque el rechazo suele venir de un Wallet caído.
/// </summary>
public sealed record ReleaseCampaignRunReservationCommand(Guid TenantId, Guid RunId);

public static class ReleaseCampaignRunReservationHandler
{
    [NonTransactional]
    public static Task Handle(
        ReleaseCampaignRunReservationCommand command,
        IWalletSpendClient wallet,
        CancellationToken ct
    ) => wallet.SettleAsync(command.TenantId, WalletReferenceTypes.CampaignRun, command.RunId, 0, ct);
}
