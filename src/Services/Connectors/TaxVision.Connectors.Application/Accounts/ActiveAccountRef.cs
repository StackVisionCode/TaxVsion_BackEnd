using TaxVision.Connectors.Domain.Shared;

namespace TaxVision.Connectors.Application.Accounts;

/// <summary>Lo mínimo para recorrer las cuentas activas sin cargar el agregado completo.</summary>
public sealed record ActiveAccountRef(Guid Id, ProviderCode ProviderCode, Guid TenantId);
