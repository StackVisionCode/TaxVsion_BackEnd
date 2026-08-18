namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Dirección postal usada en snapshots de cliente/emisor.</summary>
public sealed record Address(string Line1, string? Line2, string City, string State, string Zip, string Country);
