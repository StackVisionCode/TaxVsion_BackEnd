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

    public bool IsActive => DeletedAtUtc is null;

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static CustomerEmailAddress Create(Guid tenantId, Guid customerId, ValueObjects.EmailAddress emailAddress)
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
        };
    }

    /// <summary>Sincroniza el email con el valor actual del customer en origen. No-op si no cambió.</summary>
    public void UpdateEmail(ValueObjects.EmailAddress newEmail)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        if (EmailAddress == newEmail.NormalizedValue)
            return;

        EmailAddress = newEmail.NormalizedValue;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Marca la fila como inactiva (customer desactivado/archivado). Idempotente.</summary>
    public void SoftDelete()
    {
        if (DeletedAtUtc is not null)
            return;

        var now = DateTime.UtcNow;
        DeletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Revierte el soft-delete (customer reactivado). Acepta opcionalmente el email vigente
    /// en origen por si cambió mientras el customer estaba inactivo.
    /// </summary>
    public void Reactivate(ValueObjects.EmailAddress? newEmail = null)
    {
        DeletedAtUtc = null;
        if (newEmail is not null && EmailAddress != newEmail.NormalizedValue)
            EmailAddress = newEmail.NormalizedValue;

        UpdatedAtUtc = DateTime.UtcNow;
    }
}
