using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Catalogs;

/// <summary>Una ocupación del catálogo curado (para el picker del alta/edición de clientes).</summary>
public sealed record OccupationResponse(Guid Id, string Name);

/// <summary>Una actividad económica principal (NAICS) del catálogo curado.</summary>
public sealed record BusinessActivityResponse(Guid Id, string NaicsCode, string Description, string? Sector);

/// <summary>
/// Lista el catálogo de ocupaciones (global, no por tenant). `Search` filtra por nombre; el front lo
/// usa para el selector de ocupación del formulario de cliente. Los ids son curados: el CRM debe
/// mandar uno de estos en `OccupationId`, no texto libre.
/// </summary>
public sealed record ListOccupationsQuery(string? Search = null);

public static class ListOccupationsHandler
{
    public static async Task<Result<IReadOnlyList<OccupationResponse>>> Handle(
        ListOccupationsQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => Result.Success(await reader.ListOccupationsAsync(query.Search, ct));
}

/// <summary>Lista el catálogo de actividades económicas (NAICS). `Search` filtra por código o descripción.</summary>
public sealed record ListBusinessActivitiesQuery(string? Search = null);

public static class ListBusinessActivitiesHandler
{
    public static async Task<Result<IReadOnlyList<BusinessActivityResponse>>> Handle(
        ListBusinessActivitiesQuery query,
        ICustomerReadService reader,
        CancellationToken ct
    ) => Result.Success(await reader.ListBusinessActivitiesAsync(query.Search, ct));
}
