namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Value viaja como texto invariante: fechas yyyy-MM-dd, booleanos true/false, enums por nombre.
// Kind: solo para taxId (Ssn/Itin/Ein). Rows: solo para destinos colección (repeater → filas).
public sealed record ClientFormTargetValue(
    string TargetKey,
    int TargetSchemaVersion,
    string? Value = null,
    ClientFormAddressValue? Address = null,
    string? Kind = null,
    IReadOnlyList<ClientFormTargetRow>? Rows = null
);
