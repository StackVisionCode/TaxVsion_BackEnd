namespace BuildingBlocks.Messaging.ClientFormsIntegrationEvents;

public sealed record ClientFormAddressValue(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode
);
