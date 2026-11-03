using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Application.Delivery;

/// <summary>Qué hacer con la copia parcial recién subida según su scan.</summary>
public enum PartialCopyScanDecision
{
    /// <summary>Disponible: se entrega.</summary>
    Deliver,

    /// <summary>Resultado final no disponible (rechazada, infectada, borrada): falla la copia, no se entrega.</summary>
    Fail,

    /// <summary>Sin resultado todavía y dentro del tope: se reintenta más tarde.</summary>
    Wait,

    /// <summary>Sin resultado pasado el tope: se entrega igual; el enlace se resuelve al abrirlo.</summary>
    DeliverUnscanned,
}

/// <summary>
/// Reglas de espera del scan en la copia parcial. Solo se espera lo desconocido (sin fila o Pending):
/// un resultado final se conoce ya, porque CloudStorage anuncia rechazos, infecciones y bloqueos.
/// </summary>
public static class PartialCopyScanGate
{
    /// <summary>
    /// Imagen de firma: si no está disponible pero ya tiene resultado final, se sigue con el sello
    /// tipográfico sin reintentar; solo se espera (hasta el tope) mientras el resultado es desconocido.
    /// </summary>
    public static bool ShouldWaitForImage(FileMetadataRef? projection, TimeSpan sinceSigned, TimeSpan maxWait) =>
        projection is not { HasScanOutcome: true } && sinceSigned <= maxWait;

    public static PartialCopyScanDecision ForUploadedCopy(
        FileMetadataRef? projection,
        TimeSpan sinceSigned,
        TimeSpan maxWait
    ) =>
        projection switch
        {
            { Status: FileScanStatus.Available } => PartialCopyScanDecision.Deliver,
            { HasScanOutcome: true } => PartialCopyScanDecision.Fail,
            _ when sinceSigned <= maxWait => PartialCopyScanDecision.Wait,
            _ => PartialCopyScanDecision.DeliverUnscanned,
        };
}
