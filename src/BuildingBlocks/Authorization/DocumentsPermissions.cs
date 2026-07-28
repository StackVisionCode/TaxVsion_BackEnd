namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos HUMANOS del servicio Documents (claim <c>perm</c>, política <c>perm:*</c>). Distintos de
/// <see cref="DocumentsServiceScopes"/> (scopes OAuth M2M entre servicios). Se asignan a roles del
/// tenant desde Auth; el catálogo global vive en Auth (PermissionCatalog).
/// </summary>
public static class DocumentsPermissions
{
    /// <summary>Configurar el perfil de marca (logo/color/pie) que se aplica a los documentos del tenant.</summary>
    public const string BrandingManage = "documents.branding.manage";
}
