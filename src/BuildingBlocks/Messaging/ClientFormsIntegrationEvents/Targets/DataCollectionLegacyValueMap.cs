namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

// Única traducción permitida de valores entre esquemas de un mismo destino: se declara aquí, nunca en código.
public sealed record DataCollectionLegacyValueMap(int FromSchemaVersion, IReadOnlyDictionary<string, string> Values);
