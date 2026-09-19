using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Api.Requests;

public sealed record CreateSenderProfileRequest(CampaignChannel Channel, string Name, string SenderRef);

public sealed record UpdateSenderProfileRequest(string Name, string SenderRef);

public sealed record SetSenderProfileStatusRequest(bool Active);

/// <summary>Selecciona el remitente de una campaña para un canal.</summary>
public sealed record SetCampaignSenderRequest(CampaignChannel Channel, Guid SenderProfileId);
