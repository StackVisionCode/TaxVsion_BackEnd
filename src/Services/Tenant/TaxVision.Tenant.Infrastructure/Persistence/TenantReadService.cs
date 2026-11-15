using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Infrastructure.Persistence;

public sealed class TenantReadService(TenantDbContext db) : ITenantReadService
{
    public async Task<IReadOnlyList<TenantResponse>> GetPageAsync(int page, int size, CancellationToken ct = default)
    {
        var rows = await db
            .Tenants.AsNoTracking()
            .Where(t => t.Kind == TenantKind.Customer)
            .OrderBy(t => t.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.SubDomain,
                t.DefaultTimeZoneId,
                t.Status,
                t.Version,
            })
            .ToListAsync(ct);

        // Misma regla que TenantStatusChanged: activa solo si el estado es Active.
        return rows.Select(t => new TenantResponse(
                t.Id,
                t.Name,
                t.SubDomain,
                t.DefaultTimeZoneId,
                t.Status.ToString(),
                t.Status == EnumTenantStatus.TenantStatus.Active,
                t.Version
            ))
            .ToList();
    }
}
