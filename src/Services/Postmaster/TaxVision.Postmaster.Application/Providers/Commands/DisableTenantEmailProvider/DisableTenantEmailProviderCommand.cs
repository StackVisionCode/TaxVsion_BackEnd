namespace TaxVision.Postmaster.Application.Providers.Commands.DisableTenantEmailProvider;

/// <summary>DELETE del provider del tenant = soft-delete (Enabled=false), nunca borra la fila.</summary>
public sealed record DisableTenantEmailProviderCommand(Guid TenantId, string ProviderCode);
