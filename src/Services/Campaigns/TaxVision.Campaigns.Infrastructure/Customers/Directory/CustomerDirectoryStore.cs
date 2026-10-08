using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Infrastructure.Persistence;

namespace TaxVision.Campaigns.Infrastructure.Customers.Directory;

/// <summary>
/// Store EF de la proyección del directorio de clientes. Las escrituras solo preparan cambios en el
/// <see cref="CampaignsDbContext"/>; el consumer hace <c>SaveChangesAsync</c>. Las lecturas se saltan el
/// filtro multi-tenant global filtrando explícitamente por <c>TenantId</c> (igual criterio, pero deja claro
/// el alcance en una proyección mantenida por consumers de background).
/// </summary>
public sealed class CustomerDirectoryStore(CampaignsDbContext db) : ICustomerDirectoryStore
{
    public async Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default)
    {
        var row = await db.Set<CustomerDirectoryEntry>()
            .Where(e => e.TenantId == tenantId && e.CustomerId == customerId)
            .Select(e => (DateTime?)e.Version)
            .FirstOrDefaultAsync(ct);
        return row;
    }

    public async Task UpsertAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        string email,
        string? phoneE164,
        string status,
        DateTime version,
        CancellationToken ct = default
    )
    {
        var existing = await FindEntryAsync(tenantId, customerId, ct);
        if (existing is null)
        {
            await db.Set<CustomerDirectoryEntry>()
                .AddAsync(
                    CustomerDirectoryEntry.Create(tenantId, customerId, displayName, email, phoneE164, status, version),
                    ct
                );
            return;
        }

        existing.UpdateDetails(displayName, email, phoneE164, version);
        existing.SetStatus(status, version);
    }

    public async Task UpdateDetailsAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        string email,
        string? phoneE164,
        DateTime version,
        CancellationToken ct = default
    )
    {
        var existing = await FindEntryAsync(tenantId, customerId, ct);
        if (existing is null)
        {
            // Updated llegó antes que Created (reordenado) o sin sembrar: lo creamos Activo.
            await db.Set<CustomerDirectoryEntry>()
                .AddAsync(
                    CustomerDirectoryEntry.Create(
                        tenantId,
                        customerId,
                        displayName,
                        email,
                        phoneE164,
                        CustomerDirectoryStatus.Active,
                        version
                    ),
                    ct
                );
            return;
        }

        existing.UpdateDetails(displayName, email, phoneE164, version);
    }

    public async Task SetStatusAsync(
        Guid tenantId,
        Guid customerId,
        string status,
        DateTime version,
        CancellationToken ct = default
    )
    {
        var existing = await FindEntryAsync(tenantId, customerId, ct);
        existing?.SetStatus(status, version);
    }

    public async Task<CustomerDirectoryRecord?> FindByEmailAsync(
        Guid tenantId,
        string email,
        CancellationToken ct = default
    )
    {
        var normalized = CustomerDirectoryEntry.Normalize(email);
        return await db.Set<CustomerDirectoryEntry>()
            .Where(e => e.TenantId == tenantId && e.Email == normalized)
            .Select(e => new CustomerDirectoryRecord(e.CustomerId, e.DisplayName, e.Email, e.PhoneE164, e.Status))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CustomerDirectoryRecord>> ListActiveAsync(
        Guid tenantId,
        CancellationToken ct = default
    )
    {
        return await db.Set<CustomerDirectoryEntry>()
            .Where(e => e.TenantId == tenantId && e.Status == CustomerDirectoryStatus.Active)
            .Select(e => new CustomerDirectoryRecord(e.CustomerId, e.DisplayName, e.Email, e.PhoneE164, e.Status))
            .ToListAsync(ct);
    }

    private Task<CustomerDirectoryEntry?> FindEntryAsync(Guid tenantId, Guid customerId, CancellationToken ct) =>
        db.Set<CustomerDirectoryEntry>()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.CustomerId == customerId, ct);
}
