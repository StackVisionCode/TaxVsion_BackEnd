namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Resuelve nombres/codigos de catalogo (Occupation, NAICS) a sus Ids.
/// Decision arquitectonica: si no encuentra, devuelve null. El catalogo es curado y NO se crean
/// entradas silenciosamente desde el import (cambio explicito vs legacy CRMTAXPROBACKEND).
/// </summary>
public interface ICatalogResolver
{
    Task<Guid?> ResolveOccupationIdAsync(string occupationName, CancellationToken ct);

    Task<Guid?> ResolvePrincipalBusinessActivityIdAsync(string naicsCode, CancellationToken ct);
}
