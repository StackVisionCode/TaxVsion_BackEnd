namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Correlación de negocio de una solicitud de firma con el origen que la pidió (P-18).
/// Aditivo: Signature la guarda al crear la solicitud desde un comando y la devuelve en
/// los hechos de resultado para que el servicio emisor correlacione sin acoplarse al Id interno.
/// </summary>
public sealed record SignatureExternalReference
{
    public required string SourceService { get; init; }
    public required string SourceType { get; init; }
    public required string SourceId { get; init; }
}
