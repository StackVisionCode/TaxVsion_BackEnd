namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>
/// Selección de remitente de una campaña para UN canal (<c>SenderSelection</c> del Domain_Design §4/§6):
/// referencia a un <c>SenderProfile</c> por id. Entidad hija de <see cref="Campaign"/> — se crea/muta solo
/// desde el aggregate. Lleva <c>TenantId</c> como columna pero NO es <c>ITenantOwned</c> (se carga vía el
/// padre, igual que <c>CampaignRecipient</c>/<c>ContactListMember</c>).
/// </summary>
public sealed class CampaignSenderSelection
{
    public Guid Id { get; }
    public Guid CampaignId { get; }
    public Guid TenantId { get; }
    public CampaignChannel Channel { get; private set; }
    public Guid SenderProfileId { get; private set; }

    private CampaignSenderSelection() { }

    internal CampaignSenderSelection(Guid campaignId, Guid tenantId, CampaignChannel channel, Guid senderProfileId)
    {
        Id = Guid.NewGuid();
        CampaignId = campaignId;
        TenantId = tenantId;
        Channel = channel;
        SenderProfileId = senderProfileId;
    }

    internal void Point(Guid senderProfileId) => SenderProfileId = senderProfileId;
}
