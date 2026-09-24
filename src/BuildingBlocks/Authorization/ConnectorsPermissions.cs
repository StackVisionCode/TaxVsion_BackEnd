namespace BuildingBlocks.Authorization;

public static class ConnectorsPermissions
{
    public const string AccountsWrite = "connectors.accounts.write";
    public const string AccountsRead = "connectors.accounts.read";

    /// <summary>
    /// Conectar/administrar SOLO el buzón personal propio (== email de login). Delegable por el
    /// TenantAdmin a empleados sin darles <see cref="AccountsWrite"/> (que administra el de oficina y
    /// cualquiera). AccountsWrite lo incluye implícitamente.
    /// </summary>
    public const string AccountsConnectOwn = "connectors.accounts.connect_own";

    /// <summary>Ver el buzón de oficina y su correo. Por defecto ON en el empleado; deny per-usuario para dejarlo solo con su personal. AccountsWrite lo incluye.</summary>
    public const string AccountsOfficeRead = "connectors.accounts.office.read";
}
