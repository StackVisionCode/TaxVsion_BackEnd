using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Application.Runs;

/// <summary>
/// Qué canales se cobran contra el Wallet. <b>Push NO se cobra</b> (notificación del sistema), y
/// <b>WhatsApp está oculto</b> por ahora (sin ejecutor). Solo Email y SMS son facturables. Fuente única
/// para el gate de cobro, la liquidación y el preview de costo — los tres deben contar lo mismo.
/// </summary>
public static class BillableChannels
{
    public static bool IsBillable(CampaignChannel channel) =>
        channel is CampaignChannel.Email or CampaignChannel.Sms;
}
