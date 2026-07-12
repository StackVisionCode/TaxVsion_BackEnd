namespace BuildingBlocks.Authorization;

public static class CustomersPermissions
{
    public const string View = "customers.view";
    public const string Manage = "customers.manage";

    /// <summary>
    /// Revela el SSN/ITIN/EIN en claro de un customer. Separado de <see cref="Manage"/> a
    /// propósito — editar un fiscal profile no implica poder ver el identificador completo,
    /// y viceversa. TenantAdmin/PlatformAdmin siempre pasan (ver ClaimsPrincipalExtensions.HasPermission),
    /// el resto necesita este permiso otorgado explícitamente.
    /// </summary>
    public const string FiscalProfileReveal = "customers.fiscalprofile.reveal";
}
