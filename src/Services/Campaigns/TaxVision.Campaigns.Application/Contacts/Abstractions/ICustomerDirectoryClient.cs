namespace TaxVision.Campaigns.Application.Contacts.Abstractions;

/// <summary>Resultado de intentar provisionar un contacto importado como cliente en el servicio Customer.</summary>
public enum CustomerProvisionOutcome
{
    /// <summary>Se creó el cliente (201).</summary>
    Created,

    /// <summary>Ya existía un cliente con ese email en el tenant (409) — no es error.</summary>
    AlreadyExisted,

    /// <summary>La fila no tiene email; Customer exige <c>PrimaryEmail</c>, así que no se puede crear.</summary>
    SkippedNoEmail,

    /// <summary>El usuario que importa no tiene permiso para crear clientes (403/401 desde Customer).</summary>
    Forbidden,

    /// <summary>Customer no respondió / error no esperado. El import no falla; se reporta en el resumen.</summary>
    Unavailable,
}

/// <summary>Desenlace de una provisión + el id del cliente cuando se conoce.</summary>
public sealed record CustomerProvisionResult(CustomerProvisionOutcome Outcome, Guid? CustomerId = null);

/// <summary>
/// Puerto hacia el servicio <c>Customer</c> para CREAR clientes a partir de contactos importados, en
/// nombre del usuario que importa (on-behalf-of): la implementación reenvía el bearer de la petición
/// actual a <c>POST /customers</c> (endpoint que acepta actores humanos). NO usa token de servicio porque
/// ese endpoint no admite <c>ActorType.Service</c>. Campaigns NO es dueño del directorio de clientes:
/// solo lo alimenta y luego lo referencia por id (ver <see cref="ICustomerAudienceClient"/>).
/// </summary>
public interface ICustomerDirectoryClient
{
    /// <summary>
    /// Crea (o detecta como existente) un cliente Individual con el email como identidad. El nombre se
    /// parte en nombre/apellido de forma tolerante; si no hay email, devuelve <see cref="CustomerProvisionOutcome.SkippedNoEmail"/>.
    /// </summary>
    Task<CustomerProvisionResult> CreateIndividualAsync(
        Guid tenantId,
        string? callerBearerToken,
        string? name,
        string? email,
        string? phoneE164,
        CancellationToken ct = default
    );
}
