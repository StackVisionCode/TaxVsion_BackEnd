using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Web.ActorTypeAuthorization;

/// <summary>
/// Registro de la fuente de permisos de la Capa 2 (<see cref="HasPermissionAttribute"/>). Los 17
/// microservicios tenían este mismo <c>if/else</c> copiado byte a byte en su <c>Program.cs</c>.
/// </summary>
public static class UserPermissionsSourceRegistration
{
    private const string ProjectionMode = "Projection";
    private const string ConfigurationKey = "Authorization:PermissionsSource";

    /// <summary>
    /// Elige entre <see cref="ProjectionPermissionsSource"/> (lee la proyección local que mantienen
    /// los eventos de Auth, con enforcement de <c>perm_v</c>) y <see cref="JwtEmbeddedPermissionsSource"/>
    /// (lee el claim <c>perm</c> del token).
    ///
    /// <para>
    /// H-05 — el modo <c>Jwt</c> quedó <b>inoperante para usuarios humanos</b> desde que la Fase
    /// 7.5.10 sacó el claim <c>perm</c> de <c>JwtTokenGenerator.Generate()</c>: la fuente busca un
    /// claim que ya nadie emite, así que todo <c>[HasPermission]</c> responde 403 sin decir por qué.
    /// Por eso, si el servicio declara aunque sea un endpoint con <c>[HasPermission]</c> y la
    /// configuración no pide <c>Projection</c>, esto <b>revienta al arrancar</b> en vez de denegar en
    /// silencio en runtime. Un servicio que no gatea nada por permiso sigue pudiendo arrancar en modo
    /// <c>Jwt</c> sin problema.
    /// </para>
    /// </summary>
    /// <param name="endpointsAssembly">
    /// El ensamblado con los controllers del servicio — normalmente <c>Assembly.GetExecutingAssembly()</c>
    /// desde su <c>Program.cs</c>. Es lo que se inspecciona para saber si hay <c>[HasPermission]</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// El servicio gatea endpoints por permiso pero no está configurado en modo <c>Projection</c>.
    /// </exception>
    public static IServiceCollection AddUserPermissionsSource(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly endpointsAssembly
    )
    {
        // ProjectionPermissionsSource cachea en memoria el resultado por usuario+perm_v.
        services.AddMemoryCache();

        if (string.Equals(configuration[ConfigurationKey], ProjectionMode, StringComparison.Ordinal))
        {
            services.AddScoped<IUserPermissionsSource, ProjectionPermissionsSource>();
            return services;
        }

        var gated = FindPermissionGatedEndpoints(endpointsAssembly);
        if (gated.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{ConfigurationKey}' vale '{configuration[ConfigurationKey] ?? "(ausente)"}', pero "
                    + $"{endpointsAssembly.GetName().Name} declara {gated.Count} endpoint(s) con [HasPermission] "
                    + $"— por ejemplo {string.Join(", ", gated.Take(3))}. El claim 'perm' ya no se emite "
                    + $"(RBAC Fase 7.5.10), así que en este modo esos endpoints responderían 403 siempre. "
                    + $"Poné '{ConfigurationKey}' = '{ProjectionMode}'."
            );
        }

        services.AddScoped<IUserPermissionsSource, JwtEmbeddedPermissionsSource>();
        return services;
    }

    private static List<string> FindPermissionGatedEndpoints(Assembly endpointsAssembly) =>
        endpointsAssembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes<HasPermissionAttribute>(inherit: true).Any())
            .Select(type => type.Name)
            .Concat(
                endpointsAssembly
                    .GetTypes()
                    .SelectMany(type =>
                        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .Where(method => method.GetCustomAttributes<HasPermissionAttribute>(inherit: true).Any())
                            .Select(method => $"{type.Name}.{method.Name}")
                    )
            )
            .Order(StringComparer.Ordinal)
            .ToList();
}
