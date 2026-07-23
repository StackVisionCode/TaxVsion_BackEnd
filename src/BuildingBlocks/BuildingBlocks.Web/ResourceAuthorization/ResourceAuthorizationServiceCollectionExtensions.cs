using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.ResourceAuthorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — wiring de resource ownership, mismo criterio que
/// <c>AddActorTypeAuthorization()</c> (una línea por Program.cs, la lógica vive acá).
/// </summary>
public static class ResourceAuthorizationServiceCollectionExtensions
{
    /// <summary>Registra <see cref="ResourceOwnershipOptions"/> — llamar una sola vez por servicio.</summary>
    public static IServiceCollection AddResourceOwnershipOptions(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<ResourceOwnershipOptions>(configuration.GetSection(ResourceOwnershipOptions.SectionName));
        return services;
    }

    /// <summary>
    /// Registra el <see cref="IAuthorizationHandler"/> de ownership para <typeparamref name="TResource"/>.
    /// Se llama una vez por cada tipo de aggregate que participa de Fase 4 en este servicio.
    /// </summary>
    /// <param name="managePermission">
    /// Código del permiso que permite operar sobre recursos de OTROS usuarios (override de
    /// ownership) para este tipo de recurso. <c>null</c> si el recurso no tiene override — solo el
    /// creador (o PlatformAdmin) puede operar sobre él.
    /// </param>
    public static IServiceCollection AddOwnershipAuthorization<TResource>(
        this IServiceCollection services,
        string? managePermission = null
    )
        where TResource : IHasOwner =>
        services.AddScoped<IAuthorizationHandler>(sp => new IsOwnerOrHasManageHandler<TResource>(
            managePermission,
            sp.GetRequiredService<IUserPermissionsSource>(),
            sp.GetRequiredService<AuthorizationMetrics>()
        ));
}
