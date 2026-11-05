using BuildingBlocks.Domain;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Projections;

/// <summary>
/// Proyección local del email de un cliente registrado en Customer, alimentada por los
/// eventos <c>CustomerCreated</c>/<c>CustomerUpdated</c>/<c>CustomerDeactivated</c>/
/// <c>CustomerActivated</c>/<c>CustomerArchived</c>/<c>CustomerReactivated</c>.
///
/// <para>
/// Usada por el consumer de <c>raw_message_received</c> (Fase 4) para resolver qué
/// customer envió un correo entrante a partir del remitente (<c>From</c>).
/// </para>
///
/// <para>
/// Por diseño de esta fase, solo se proyecta el email primario del customer
/// (<see cref="CustomerEmailSource.CustomerPrimary"/>) — no hay evento de Customer que
/// publique emails secundarios o de contacto.
/// </para>
/// </summary>
public sealed class CustomerEmailAddress : ITenantOwned
{
    private CustomerEmailAddress() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string EmailAddress { get; private set; } = default!;
    public bool IsPrimary { get; private set; }
    public CustomerEmailSource Source { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    // Marcas del último evento aplicado (hora de Customer); lo más viejo que llegue tarde se ignora.
    public DateTime? LastEmailEventOnUtc { get; private set; }
    public DateTime? LastStatusEventOnUtc { get; private set; }

    public bool IsActive => DeletedAtUtc is null;

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static CustomerEmailAddress Create(
        Guid tenantId,
        Guid customerId,
        ValueObjects.EmailAddress emailAddress,
        DateTime? occurredOnUtc = null
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));
        ArgumentNullException.ThrowIfNull(emailAddress);

        var now = DateTime.UtcNow;
        return new CustomerEmailAddress
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            EmailAddress = emailAddress.NormalizedValue,
            IsPrimary = true,
            Source = CustomerEmailSource.CustomerPrimary,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            DeletedAtUtc = null,
            LastEmailEventOnUtc = occurredOnUtc,
            LastStatusEventOnUtc = occurredOnUtc,
        };
    }

    /// <summary>
    /// Sincroniza el email con el valor del customer en origen a <paramref name="occurredOnUtc"/>. Devuelve
    /// <c>false</c> si el cambio es más viejo que el último aplicado (evento fuera de orden) y no toca nada.
    /// </summary>
    public bool UpdateEmail(ValueObjects.EmailAddress newEmail, DateTime occurredOnUtc)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        if (IsOlder(occurredOnUtc, LastEmailEventOnUtc))
            return false;

        LastEmailEventOnUtc = occurredOnUtc;
        if (EmailAddress != newEmail.NormalizedValue)
        {
            EmailAddress = newEmail.NormalizedValue;
            UpdatedAtUtc = DateTime.UtcNow;
        }
        return true;
    }

    /// <summary>Marca la fila como inactiva (customer desactivado/archivado). Idempotente; ignora lo más viejo.</summary>
    public bool SoftDelete(DateTime occurredOnUtc)
    {
        if (IsOlder(occurredOnUtc, LastStatusEventOnUtc))
            return false;

        LastStatusEventOnUtc = occurredOnUtc;
        if (DeletedAtUtc is not null)
            return true;

        var now = DateTime.UtcNow;
        DeletedAtUtc = now;
        UpdatedAtUtc = now;
        return true;
    }

    /// <summary>
    /// Revierte el soft-delete (customer reactivado). Acepta opcionalmente el email vigente en origen por si
    /// cambió mientras el customer estaba inactivo. Email y estado se ordenan por separado.
    /// </summary>
    public bool Reactivate(DateTime occurredOnUtc, ValueObjects.EmailAddress? newEmail = null)
    {
        if (newEmail is not null)
            UpdateEmail(newEmail, occurredOnUtc);

        if (IsOlder(occurredOnUtc, LastStatusEventOnUtc))
            return false;

        LastStatusEventOnUtc = occurredOnUtc;
        DeletedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static bool IsOlder(DateTime occurredOnUtc, DateTime? lastAppliedUtc) =>
        lastAppliedUtc is not null && occurredOnUtc < lastAppliedUtc;
}
