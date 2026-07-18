namespace TaxVision.CloudStorage.Domain.Sharing;

/// <summary>Fase C3 solo crea links sobre File. Folder queda modelado para la Fase C4 (recursivo).</summary>
public enum ShareResourceType
{
    File,
    Folder,
}

public enum ShareVisibility
{
    /// <summary>Sin autenticacion. Deshabilitado por defecto por tenant (datos fiscales) — ver TenantStorageLimit.AllowPublicShareLinks.</summary>
    Public,

    /// <summary>Cualquier usuario autenticado del mismo tenant.</summary>
    TenantOnly,

    /// <summary>Solo los TenantUserId listados en ShareLink.Recipients.</summary>
    SpecificUsers,

    /// <summary>Portal de clientes del tenant — todos si no hay recipients, o solo los CustomerId listados.</summary>
    TenantCustomers,

    /// <summary>Direcciones de email externas sin cuenta — se resuelven por el flujo publico + verificacion de email.</summary>
    ExternalRecipients,
}

public enum SharePermission
{
    View,
    Preview,
    Download,

    /// <summary>Solo asignable por un actor con cloudstorage.share.manage (ver CreateShareLinkHandler).</summary>
    Upload,

    /// <summary>Solo asignable por un actor con cloudstorage.share.manage (ver CreateShareLinkHandler).</summary>
    EditMetadata,
}

/// <summary>
/// Estado persistido y autoritativo. Deliberadamente NO incluye "Expired": la
/// expiracion se evalua siempre en vivo contra ExpiresAtUtc (ver IsExpired) para
/// no depender de un job que mantenga el campo al dia — ver EffectiveStatus.
/// </summary>
public enum ShareStatus
{
    Active,
    Revoked,
    Exhausted,
}

/// <summary>Vista de solo lectura para listados/API — combina Status persistido + expiracion evaluada en vivo.</summary>
public enum ShareLinkEffectiveStatus
{
    Active,
    Expired,
    Revoked,
    Exhausted,
}
