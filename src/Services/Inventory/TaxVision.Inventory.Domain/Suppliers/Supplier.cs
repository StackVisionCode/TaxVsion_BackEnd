using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Inventory.Domain.Suppliers;

/// <summary>Proveedor del tenant. Tenant-owned, soft-delete.</summary>
public sealed class Supplier : TenantEntity
{
    public const int NameMax = 200;

    public Guid TaxUserId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? TaxId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    private Supplier() { }

    public static Result<Supplier> Create(
        Guid tenantId,
        Guid taxUserId,
        string name,
        string? contactName,
        string? email,
        string? phone,
        string? address,
        string? taxId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<Supplier>(InventoryErrors.InvalidTenant);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure<Supplier>(InventoryErrors.InvalidName);

        var supplier = new Supplier
        {
            TaxUserId = taxUserId,
            Name = name.Trim(),
            ContactName = Normalize(contactName),
            Email = Normalize(email),
            Phone = Normalize(phone),
            Address = Normalize(address),
            TaxId = Normalize(taxId),
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            IsDeleted = false,
        };
        supplier.SetTenant(tenantId);
        return Result.Success(supplier);
    }

    public Result Update(
        string name,
        string? contactName,
        string? email,
        string? phone,
        string? address,
        string? taxId,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMax)
            return Result.Failure(InventoryErrors.InvalidName);

        Name = name.Trim();
        ContactName = Normalize(contactName);
        Email = Normalize(email);
        Phone = Normalize(phone);
        Address = Normalize(address);
        TaxId = Normalize(taxId);
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public void SetActive(bool active, DateTime nowUtc)
    {
        IsActive = active;
        UpdatedAtUtc = nowUtc;
    }

    public void SoftDelete(DateTime nowUtc)
    {
        if (IsDeleted)
            return;
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    private static string? Normalize(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
