namespace TaxVision.Signature.Domain.Projections;

/// <summary>
/// Proyección local del catálogo de clientes registrados por tenant. Se alimenta de los
/// eventos <c>CustomerCreated</c>, <c>CustomerUpdated</c>, <c>CustomerEmailChanged</c> y
/// <c>CustomerArchived</c>.
///
/// <para>
/// Regla P-14 del diseño: al agregar un firmante por email, se busca aquí si existe un
/// cliente registrado del mismo tenant con ese email, para vincular <c>MappedCustomerId</c>.
/// </para>
///
/// <para>
/// El email se almacena normalizado (trim + lowercase) para matcheo determinístico.
/// </para>
/// </summary>
public sealed class CustomerEmailProjection
{
    private CustomerEmailProjection() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string NormalizedEmail { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public bool IsArchived { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static CustomerEmailProjection ForNewCustomer(
        Guid tenantId,
        Guid customerId,
        string normalizedEmail,
        string displayName
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("CustomerId is required.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("Email is required.", nameof(normalizedEmail));

        var now = DateTime.UtcNow;
        return new CustomerEmailProjection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName,
            IsArchived = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void UpdateDisplayName(string displayName)
    {
        DisplayName = displayName;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeEmail(string normalizedEmail)
    {
        NormalizedEmail = normalizedEmail;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkArchived()
    {
        IsArchived = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkReactivated()
    {
        IsArchived = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
