namespace TaxVision.Signature.Domain.Requests.ValueObjects;

/// <summary>
/// Correlación de negocio con el origen que pidió la firma desde otro servicio (P-18): servicio,
/// tipo y id del recurso de origen. Inmutable; se fija una sola vez al crear la solicitud.
/// </summary>
public sealed record ExternalReference(string SourceService, string SourceType, string SourceId);
