namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ProtectedPayload
{
    public required string Cipher { get; init; }
    public required int KeyVersion { get; init; }
    public required string Schema { get; init; }
}
