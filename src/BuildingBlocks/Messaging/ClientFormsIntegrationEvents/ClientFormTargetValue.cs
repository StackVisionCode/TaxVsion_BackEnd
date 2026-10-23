namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Value viaja como texto invariante: fechas yyyy-MM-dd, booleanos true/false, enums por nombre.
public sealed record ClientFormTargetValue(
    string TargetKey,
    int TargetSchemaVersion,
    string? Value = null,
    ClientFormAddressValue? Address = null
);
