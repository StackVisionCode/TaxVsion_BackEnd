namespace BuildingBlocks.Infrastructure.Security;

/// <summary>F25 — token M2M adquirido de Auth vía <see cref="ServiceTokenHttpAcquisition"/>.</summary>
public sealed record ServiceTokenGrant(string AccessToken, DateTime ExpiresAtUtc);
