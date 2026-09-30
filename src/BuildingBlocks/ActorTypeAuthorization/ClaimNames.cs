namespace BuildingBlocks.ActorTypeAuthorization;

/// <summary>
/// Nombres de claim del JWT que emite Auth, compartidos por los 14 microservicios .NET. Antes
/// eran strings literales repetidos a mano (y con leves inconsistencias) en cada servicio.
/// </summary>
public static class ClaimNames
{
    public const string ActorType = "actor_type";
    public const string TenantId = "tenant_id";
    public const string CustomerId = "customer_id";
    public const string Permission = "perm";

    /// <summary>Superficie del token (ver <see cref="AccessSurface"/>). Ausente en los tokens del CRM y del portal.</summary>
    public const string Surface = "surface";

    /// <summary>Epoch (segundos) de la última reautenticación (step-up). Solo en el access token que la emitió.</summary>
    public const string ReauthenticatedAt = "reauth_at";
}
