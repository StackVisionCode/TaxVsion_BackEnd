namespace TaxVision.Connectors.Api.Requests;

/// <summary>M2M: direcciones de los buzones del tenant (tiene que ser el del token).</summary>
public sealed record MailboxAddressesRequest(Guid TenantId);
