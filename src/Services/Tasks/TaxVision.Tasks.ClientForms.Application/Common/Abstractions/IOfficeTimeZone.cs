using BuildingBlocks.TimeZones;

namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

/// <summary>Zona de la oficina, para convertir fechas civiles (fecha límite de un formulario) en instantes.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct);
}
