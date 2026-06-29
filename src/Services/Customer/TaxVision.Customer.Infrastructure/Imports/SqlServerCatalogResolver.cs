using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Resuelve nombres/codigos de catalogo a Ids. Estricto: si no existe, devuelve null.
/// El catalogo es curado (171 occupations seed, 769 NAICS) y NO se crean entradas desde el import.
/// </summary>
internal sealed class SqlServerCatalogResolver(CustomerDbContext db) : ICatalogResolver
{
    public async Task<Guid?> ResolveOccupationIdAsync(string occupationName, CancellationToken ct)
    {
        var normalized = occupationName.Trim().ToLowerInvariant();
        var hit = await db
            .Occupations.AsNoTracking()
            .Where(o => o.Name.ToLower() == normalized)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);
        return hit;
    }

    public async Task<Guid?> ResolvePrincipalBusinessActivityIdAsync(string naicsCode, CancellationToken ct)
    {
        var normalized = naicsCode.Trim();
        var hit = await db
            .PrincipalBusinessActivities.AsNoTracking()
            .Where(p => p.NaicsCode == normalized)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        return hit;
    }
}
