using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;

namespace TaxVision.PaymentApp.Tests.Architecture;

/// <summary>
/// PaymentApp es el tercer servicio que acepta tokens del Account, y el único que guarda dinero: esta es la
/// lista completa de lo que su superficie puede tocar: leer su historial y bajar sus recibos. Si
/// alguien abre otro endpoint, acá se entera.
/// </summary>
public sealed class PaymentAppAccountSurfaceTests
{
    /// <summary>Acciones de PaymentApp que aceptan un token del Account.</summary>
    private static MethodInfo[] SurfaceActions() =>
        [
            .. typeof(TaxVision.PaymentApp.Api.Controllers.SaaSPaymentsController)
                .Assembly.GetTypes()
                .SelectMany(type =>
                    type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                )
                .Where(method => method.GetCustomAttribute<AllowSurfaceAttribute>() is not null)
                .OrderBy(method => $"{method.DeclaringType!.Name}.{method.Name}", StringComparer.Ordinal),
        ];

    [Fact]
    public void Only_the_billing_history_and_its_receipts_accept_account_tokens()
    {
        var allowed = SurfaceActions().Select(method => $"{method.DeclaringType!.Name}.{method.Name}").ToArray();

        Assert.Equal(["SaaSPaymentsController.GetReceipt", "SaaSPaymentsController.SearchMine"], allowed);
    }

    /// <summary>
    /// Nada de lo que el Account toca en PaymentApp mueve dinero, así que todo cae en la categoría de
    /// lectura. Si algo de acá pasara a cobrar, la categoría tendría que subir con él.
    /// </summary>
    [Fact]
    public void The_account_only_reads_here()
    {
        foreach (var action in SurfaceActions())
        {
            var attribute = action.GetCustomAttribute<RateLimitAttribute>();
            Assert.True(
                attribute is not null,
                $"{action.DeclaringType!.Name}.{action.Name} acepta tokens del Account sin política de rate limit."
            );
            Assert.Equal(RateLimitCategory.F, RateLimitPolicyCatalog.GetByName(attribute!.PolicyName).Category);
        }
    }

    // El historial es de lectura y de su propio tenant: ni cobra, ni reembolsa, ni cruza tenants.
    [Fact]
    public void The_billing_history_is_a_tenant_admin_read()
    {
        var action = typeof(TaxVision.PaymentApp.Api.Controllers.SaaSPaymentsController).GetMethod("SearchMine")!;

        Assert.Contains(AccessSurface.Account, action.GetCustomAttribute<AllowSurfaceAttribute>()!.Surfaces);
        Assert.Equal(
            new[] { ActorType.TenantAdmin, ActorType.PlatformAdmin },
            action.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
    }

    /// <summary>
    /// El tenant sale siempre del token. Si un endpoint del Account empezara a aceptarlo por la ruta, la
    /// query o el body, un admin podría pedir la oficina de otro con solo cambiar el identificador.
    /// </summary>
    [Fact]
    public void No_account_endpoint_takes_the_tenant_from_the_caller()
    {
        foreach (var action in SurfaceActions())
        {
            var name = $"{action.DeclaringType!.Name}.{action.Name}";
            foreach (var parameter in action.GetParameters())
            {
                Assert.False(
                    parameter.Name!.Contains("tenant", StringComparison.OrdinalIgnoreCase),
                    $"{name} recibe el tenant de quien llama: {parameter.Name}."
                );

                if (parameter.ParameterType.Namespace?.StartsWith("TaxVision", StringComparison.Ordinal) != true)
                    continue;

                var carried = parameter
                    .ParameterType.GetProperties()
                    .FirstOrDefault(property => property.Name.Contains("tenant", StringComparison.OrdinalIgnoreCase));
                Assert.True(carried is null, $"{name} recibe el tenant dentro de {parameter.ParameterType.Name}.");
            }
        }
    }
}
