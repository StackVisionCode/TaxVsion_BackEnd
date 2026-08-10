namespace TaxVision.Documents.Domain.ValueObjects;

/// <summary>Recurso externo del servicio dueño al que pertenece la generación (p.ej. una factura).</summary>
public sealed record GenerationOwner(string OwnerType, Guid OwnerId);
