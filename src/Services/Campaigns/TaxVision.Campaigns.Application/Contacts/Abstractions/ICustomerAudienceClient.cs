namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>Un cliente del directorio de Customer como miembro de audiencia (email/teléfono resueltos).</summary>
public sealed record CustomerAudienceMember(Guid CustomerId, string? Email, string? PhoneE164);

/// <summary>
/// Puerto hacia el servicio <c>Customer</c> (M2M) para usar clientes activos como audiencia de campaña
/// (<c>AudienceSpec.Clients</c>, Domain_Design §6). La implementación llama al endpoint interno
/// <c>GET /internal/customers/list</c> con un token de servicio del tenant (ActorType.Service). Campaigns
/// NO es dueño del directorio de clientes — solo lo referencia por id.
/// </summary>
public interface ICustomerAudienceClient
{
    /// <summary>Clientes activos del tenant (paginado internamente). Vacío si el servicio no responde (fail-open a "sin clientes").</summary>
    Task<IReadOnlyList<CustomerAudienceMember>> GetActiveCustomersAsync(Guid tenantId, CancellationToken ct = default);
}
