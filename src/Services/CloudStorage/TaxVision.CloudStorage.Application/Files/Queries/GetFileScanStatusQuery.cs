using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Application.Files.Queries;

/// <summary>Sólo el veredicto: nombre, tamaño y ruta no le hacen falta a quien pregunta.</summary>
public sealed record FileScanStatusResponse(Guid FileId, string Status);

/// <param name="TenantId">Del token de servicio: un servicio no lee archivos de otro tenant.</param>
public sealed record GetFileScanStatusQuery(Guid TenantId, Guid FileId);

/// <summary>
/// Para servicios que guardaron una referencia a un archivo y necesitan saber en qué acabó el
/// escaneo. El veredicto se publica por evento una sola vez; quien registró la referencia después de
/// esa publicación no tiene otra forma de enterarse.
/// </summary>
public static class GetFileScanStatusHandler
{
    public static async Task<Result<FileScanStatusResponse>> Handle(
        GetFileScanStatusQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);

        return file is null
            ? Result.Failure<FileScanStatusResponse>(FileErrors.NotFound)
            : Result.Success(new FileScanStatusResponse(file.Id, file.Status.ToString()));
    }
}
