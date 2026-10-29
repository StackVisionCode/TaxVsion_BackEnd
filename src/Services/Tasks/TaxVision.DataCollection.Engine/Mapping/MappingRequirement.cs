namespace TaxVision.DataCollection.Engine.Mapping;

// Kind nulo: el destino es requerido para la operación sea cual sea el discriminador.
public sealed record MappingRequirement(string Operation, string? Kind = null);
