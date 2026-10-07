namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del servicio Wallet (monedero de saldo por tenant). <c>View</c> cubre ver el saldo, el
/// historial (ledger) y las tarifas vigentes; <c>Manage</c> cubre recargar el saldo. Editar el catálogo
/// de tarifas NO es un permiso de tenant: es exclusivo de <c>PlatformAdmin</c> (se gatea por actor-type en
/// el controller, no por permiso).
/// </summary>
public static class WalletPermissions
{
    /// <summary>Ver saldo, historial (ledger) y tarifas.</summary>
    public const string View = "wallet.view";

    /// <summary>Recargar el saldo (top-up). La recarga es un cobro real por Stripe.</summary>
    public const string Manage = "wallet.manage";
}
