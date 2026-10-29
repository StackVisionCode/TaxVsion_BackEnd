namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

// Un destino que no se pudo aplicar, con el código del motivo (sin valores).
public sealed record ClientFormProfileUpdateFailure(string Target, string Code);
