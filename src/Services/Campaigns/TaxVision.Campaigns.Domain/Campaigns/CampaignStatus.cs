namespace TaxVision.Campaigns.Domain.Campaigns;

/// <summary>
/// Ciclo de vida de la <b>definición</b> de una campaña (ver
/// <c>documents/architecture/campaigns/campaigns/State_Machines.md §1</c>). No incluye
/// <c>Sending</c>: la ejecución vive en <c>CampaignRun</c> (una máquina aparte, fase posterior).
/// </summary>
public enum CampaignStatus
{
    Draft = 0,
    Ready = 1,
    Scheduled = 2,
    Archived = 3,
}
