namespace BuildingBlocks.Tenancy;

public enum TenantKind
{
    Customer,
    Platform,
}

public static class PlatformTenant
{
    public static readonly Guid Id = Guid.Parse("8f58a521-4c25-4d91-9f4e-7ad5df14c001");

    public const string Name = "TaxVision Platform";
    public const string SubDomain = "platform-internal";
}
