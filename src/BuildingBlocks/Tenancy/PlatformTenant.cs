namespace BuildingBlocks.Tenancy;

public enum TenantKind
{
    Customer,
    Platform,
}

public static class PlatformTenant
{
    public static readonly Guid Id = Guid.Parse("8f58a521-4c25-4d91-9f4e-7ad5df14c001");

    // Neutro a propósito: la marca de la plataforma sale de PlatformSettings y cambia; este nombre vive
    // en HasData (Auth y Tenant), donde cada cambio exige una migración.
    public const string Name = "Platform";
    public const string SubDomain = "platform-internal";
}
