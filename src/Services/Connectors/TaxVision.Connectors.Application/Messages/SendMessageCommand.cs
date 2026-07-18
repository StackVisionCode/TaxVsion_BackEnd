using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Application.Messages;

/// <summary>M2M — <c>POST /connectors/accounts/{accountId}/send</c> (D3 §3.7).</summary>
public sealed record SendMessageCommand(Guid TenantId, Guid AccountId, OutboundMessage Message);
