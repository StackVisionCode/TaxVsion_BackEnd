namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Una fila de un destino colección: campo del catálogo -> texto invariante (fechas yyyy-MM-dd, enums por nombre).
public sealed record ClientFormTargetRow(IReadOnlyDictionary<string, string> Fields);
