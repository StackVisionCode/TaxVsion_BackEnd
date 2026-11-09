namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>Un cliente del directorio de Customer como miembro de audiencia (email/teléfono resueltos).</summary>
public sealed record CustomerAudienceMember(
    Guid CustomerId,
    string? Email,
    string? PhoneE164,
    string? DisplayName = null
);

/// <summary>
/// Clientes leídos del directorio. <see cref="IsComplete"/> = false si la lectura se cortó (sin token,
/// error HTTP, tope de páginas): los miembros son parciales y no sirven como audiencia.
/// </summary>
public sealed record CustomerAudienceResult(
    IReadOnlyList<CustomerAudienceMember> Members,
    bool IsComplete,
    string? IncompleteReason = null
);

/// <summary>
/// Puerto hacia el servicio <c>Customer</c> (M2M) para usar clientes activos como audiencia de campaña
/// (<c>AudienceSpec.Clients</c>, Domain_Design §6). La implementación llama al endpoint interno
/// <c>GET /internal/customers/list</c> con un token de servicio del tenant (ActorType.Service). Campaigns
/// NO es dueño del directorio de clientes — solo lo referencia por id.
/// </summary>
public interface ICustomerAudienceClient
{
    /// <summary>Clientes activos del tenant (paginado internamente), con la marca de si la lectura fue completa.</summary>
    Task<CustomerAudienceResult> GetActiveCustomersAsync(Guid tenantId, CancellationToken ct = default);
}
