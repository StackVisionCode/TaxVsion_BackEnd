namespace TaxVision.Billing.Api.Authorization;

/// <summary>Permisos que exigen los endpoints de facturación tenant→cliente (Invoices, IssuerProfile, InvoiceSettings).
/// Son OPERATIVOS (no peligrosos): el TenantAdmin los recibe por su bundle de sistema y el empleado por
/// el suyo (PermissionCatalog.cs: invoicing.view / invoicing.manage). NO confundir con billing.* de Auth,
/// que es el billing de SUSCRIPCIÓN SaaS (peligroso/admin-only, RBAC Fase 2) y no se usa en este servicio.
/// La proyección local de permisos (AuthzUserPermissionsProjection) guarda todos los códigos sin filtrar,
/// así que estos se enforzan por perm_v sin llamar a Auth en el hot path.</summary>
public static class InvoicingPermissions
{
    public const string View = "invoicing.view";
    public const string Manage = "invoicing.manage";

    /// <summary>
    /// Editar los ajustes de facturación (plazo de pago, instrucciones de pago, pie). Sale de
    /// <see cref="Manage"/> porque no es trabajo de facturación diaria: cómo cobra la oficina lo decide
    /// el dueño, no el preparador que emite la factura. El emisor legal ya no se edita aquí (vive en Tenant).
    /// </summary>
    public const string IssuerManage = "invoicing.issuer.manage";
}
