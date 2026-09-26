namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Gente que ocupa cupo en la oficina: activos más invitaciones sin aceptar.</summary>
public sealed record TenantUserCount(int ActiveUsers, int PendingInvitations)
{
    public int Occupied => ActiveUsers + PendingInvitations;
}

/// <summary>
/// M2M contra Auth, el único que sabe cuántos usuarios activos hay. Subscription lo necesita para no
/// agendar un downgrade que dejaría a la oficina por encima del cupo del plan destino. Devuelve null
/// cuando Auth no responde: el llamador decide si eso bloquea o no.
/// </summary>
public interface ITenantUserCountClient
{
    Task<TenantUserCount?> GetAsync(Guid tenantId, CancellationToken ct = default);
}
