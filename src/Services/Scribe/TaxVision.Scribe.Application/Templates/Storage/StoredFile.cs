using BuildingBlocks.Results;

namespace TaxVision.Scribe.Application.Templates.Storage;

/// <summary>
/// Distingue "el archivo ya no existe" (404 de CloudStorage o de MinIO) de un fallo pasajero (sin token, 5xx,
/// servicio arrancando). Solo lo primero justifica reponer un logo o un layout desde el archivo empacado.
/// </summary>
public static class StoredFile
{
    public const string NotFoundCode = "CloudStorageClient.NotFound";

    public static bool IsMissing(Error error) => error.Code == NotFoundCode;
}
