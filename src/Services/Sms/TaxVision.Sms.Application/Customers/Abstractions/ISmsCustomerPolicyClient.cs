namespace TaxVision.Sms.Application.Customers.Abstractions;

/// <param name="SmsEnabled">Null = Customer que no manda la política: no se toca la proyección.</param>
public sealed record RemoteCustomerContactPolicy(Guid TenantId, Guid CustomerId, bool? SmsEnabled);

public sealed record RemoteCustomerContactPolicyPage(IReadOnlyList<RemoteCustomerContactPolicy> Items, bool HasMore);

/// <summary>Enumera la política de contacto de todos los clientes (cross-tenant) para la reconciliación.</summary>
public interface ISmsCustomerPolicyClient
{
    /// <summary>Pide el token de la PlatformTenant. Devuelve <c>null</c> ante cualquier fallo.</summary>
    Task<RemoteCustomerContactPolicyPage?> ListPageAsync(int page, int size, CancellationToken ct = default);
}
