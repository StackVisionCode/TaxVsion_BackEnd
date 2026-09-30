using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.RateLimiting.Abstractions;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Queries;

/// <summary>
/// Estado comercial de la oficina, solo para el staff. El portal del cliente no lo recibe: un cliente
/// no tiene por qué saber si la oficina está al día con su propia suscripción.
/// </summary>
/// <param name="State">
/// <c>active</c>, <c>billing_blocked</c> (la suscripción venció o cayó en lapso) o <c>suspended</c>
/// (suspensión administrativa del tenant). Coherente con el corte real de acceso: lo decide el mismo
/// flag que mueve <c>TenantSubscriptionAccessConsumer</c>, así que <c>Expired</c> aparece como
/// bloqueado y no como activo.
/// </param>
/// <param name="CanManageBilling">Si este usuario puede tocar el plan o el pago. Nunca se deriva del
/// actor type: sale de los permisos efectivos, que ya restan los denies.</param>
public sealed record AccessSubscriptionResponse(string State, bool CanManageBilling);

/// <summary>
/// El bootstrap único de acceso (A5 / §R.4.1). Con esto —y nada más— el CRM arma su sidebar, sus
/// guards y sus botones, y el Portal sus áreas. Antes había que combinar <c>GET /auth/me</c> (que trae
/// el usuario entero y los límites del plan) con <c>GET /auth/me/effective-access</c> (pensado para
/// debugging) y <c>GET /subscriptions/me</c>.
/// </summary>
/// <param name="EffectivePermissions">La unión de los roles activos menos los denies vigentes.</param>
/// <param name="Modules">Módulos habilitados por el plan del tenant.</param>
/// <param name="PermissionsVersion">El <c>perm_v</c> del usuario. Si el token trae uno menor, el
/// siguiente request responde <c>401 Auth.TokenStale</c>: el frontend refresca y vuelve a pedir esto.</param>
/// <param name="EntitlementsRevision">Revisión del snapshot de entitlements del tenant. No viaja en el
/// JWT a propósito (un cambio de plan no debe invalidar tokens); sirve para saber que este bootstrap
/// quedó viejo.</param>
public sealed record MyAccessResponse(
    string ActorType,
    IReadOnlyList<string> EffectivePermissions,
    IReadOnlyList<string> Modules,
    int PermissionsVersion,
    long EntitlementsRevision,
    AccessSubscriptionResponse? Subscription
)
{
    /// <summary>ETag débil del contenido. El controller responde 304 si el <c>If-None-Match</c> del
    /// cliente coincide: el CRM puede pedir este endpoint en cada navegación sin costo.</summary>
    public string ETag { get; init; } = string.Empty;
}

/// <param name="Surface">
/// Superficie del token (claim <c>surface</c>), solo para el registro de la decisión. El rechazo por
/// superficie NO se hace acá: lo hace <c>SurfaceAuthorizationFilter</c> antes de llegar al handler,
/// porque el endpoint no declara <c>[AllowSurface]</c> — un token del Account del Landing nunca obtiene
/// el bootstrap del CRM (§R.4.1).
/// </param>
public sealed record GetMyAccessQuery(Guid UserId, string? Surface = null);

public static class GetMyAccessHandler
{
    /// <summary>Estados posibles de <see cref="AccessSubscriptionResponse.State"/>.</summary>
    public const string StateActive = "active";
    public const string StateBillingBlocked = "billing_blocked";
    public const string StateSuspended = "suspended";

    /// <summary>Los dos permisos que habilitan la gestión comercial. <c>billing.view</c> no alcanza:
    /// ver la factura no es poder cambiar el plan.</summary>
    private static readonly string[] BillingManagementPermissions =
    [
        PermissionCatalog.BillingManage,
        PermissionCatalog.SubscriptionManage,
    ];

    public static async Task<Result<MyAccessResponse>> Handle(
        GetMyAccessQuery query,
        IUserRepository users,
        IRoleRepository roles,
        ITenantRegistry tenants,
        ITenantPlanLimitsStore planLimits,
        ITenantPlanCodeProjectionRepository planCodes,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<MyAccessResponse>(new Error("User.NotFound", "User does not exist."));

        var (_, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var effectivePermissions = permissions.OrderBy(code => code, StringComparer.Ordinal).ToList();

        var limits = await planLimits.GetAsync(user.TenantId, ct);
        var modules = (limits is null ? [] : JsonSerializer.Deserialize<List<string>>(limits.EnabledModulesJson) ?? [])
            .OrderBy(module => module, StringComparer.Ordinal)
            .ToList();

        // La revisión sale de la proyección de plan del tenant, que es lo que el guard de revisión del
        // gate de módulo ya usa. Sin proyección todavía (tenant nuevo) vale 0: "no sé", no "al día".
        var planCode = await planCodes.GetAsync(user.TenantId, ct);
        var entitlementsRevision = planCode?.RevisionNumber ?? 0;

        AccessSubscriptionResponse? subscription = null;
        if (user.ActorType != UserActorType.CustomerPortal)
        {
            var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
            subscription = new AccessSubscriptionResponse(
                ResolveState(tenant?.IsActive ?? false, tenant?.BillingAccessBlocked ?? false, limits),
                effectivePermissions.Intersect(BillingManagementPermissions, StringComparer.OrdinalIgnoreCase).Any()
            );
        }

        var response = new MyAccessResponse(
            user.ActorType.ToString(),
            effectivePermissions,
            modules,
            user.PermissionsVersion,
            entitlementsRevision,
            subscription
        );

        return Result.Success(response with { ETag = ComputeETag(response, query.Surface) });
    }

    /// <summary>
    /// A6.5 del plan — el estado que ve el frontend tiene que coincidir con el corte real de acceso. La
    /// suspensión administrativa manda sobre el bloqueo por facturación: es la más restrictiva y no se
    /// arregla pagando.
    /// </summary>
    public static string ResolveState(bool tenantIsActive, bool billingBlocked, Domain.Tenants.TenantPlanLimits? limits)
    {
        if (!tenantIsActive)
            return StateSuspended;
        return billingBlocked || (limits?.IsSuspendedForBilling ?? false) ? StateBillingBlocked : StateActive;
    }

    /// <summary>
    /// ETag débil sobre TODO el contenido, no solo sobre las dos versiones: un deny que se vence, un
    /// módulo que se habilita o un cambio de estado comercial pueden mover la respuesta sin mover
    /// <c>perm_v</c>. La superficie entra en el hash para que dos tokens de superficies distintas nunca
    /// se sirvan entre sí desde una caché intermedia.
    /// </summary>
    public static string ComputeETag(MyAccessResponse response, string? surface)
    {
        var material = string.Join(
            '\n',
            response.ActorType,
            surface ?? string.Empty,
            response.PermissionsVersion.ToString(CultureInfo.InvariantCulture),
            response.EntitlementsRevision.ToString(CultureInfo.InvariantCulture),
            string.Join(',', response.EffectivePermissions),
            string.Join(',', response.Modules),
            response.Subscription?.State ?? string.Empty,
            response.Subscription?.CanManageBilling.ToString() ?? string.Empty
        );

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return $"W/\"{Convert.ToHexString(digest)[..32].ToLowerInvariant()}\"";
    }
}
