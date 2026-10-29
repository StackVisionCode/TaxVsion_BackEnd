namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;

// Campo de una fila de un destino colección; las filas llegan de un repeater vía rowFields.
public sealed record DataCollectionTargetField(
    string Name,
    DataCollectionTargetType DataType,
    int? MaxLength = null,
    bool Required = false
);
