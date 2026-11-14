using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.FinancialInsights.Api.Controllers;

namespace TaxVision.FinancialInsights.Tests.Architecture;

/// <summary>
/// Cada acción declara actor, permiso y política de rate limit (método o controller), y las políticas
/// existen en el catálogo. Mismo criterio que las fitness de controllers del resto del monorepo.
/// </summary>
public sealed class FinancialInsightsControllerArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(ImportBatchesController).Assembly;

    public static TheoryData<string, string, string, ActorType> ExpectedActions =>
        new()
        {
            { "ImportBatchesController.Create", "financial.i.import_batch_create", FinancialInsightsPermissions.Import, ActorType.TenantEmployee },
            { "ImportBatchesController.List", "financial.f.import_batch_read", FinancialInsightsPermissions.View, ActorType.TenantEmployee },
            { "ImportBatchesController.Get", "financial.f.import_batch_read", FinancialInsightsPermissions.View, ActorType.TenantEmployee },
            { "ImportBatchesController.Retry", "financial.i.import_document_retry", FinancialInsightsPermissions.Import, ActorType.TenantEmployee },
            { "PortalImportBatchesController.Create", "financial.i.portal_import_batch_create", FinancialInsightsPermissions.PortalImport, ActorType.CustomerPortal },
            { "PortalImportBatchesController.List", "financial.f.import_batch_read", FinancialInsightsPermissions.PortalImport, ActorType.CustomerPortal },
            { "PortalImportBatchesController.Get", "financial.f.import_batch_read", FinancialInsightsPermissions.PortalImport, ActorType.CustomerPortal },
        };

    [Theory]
    [MemberData(nameof(ExpectedActions))]
    public void Action_declares_its_policy_permission_and_actor(
        string action,
        string policy,
        string permission,
        ActorType actor
    )
    {
        var method = FindAction(action);
        var controller = method.DeclaringType!;

        var rateLimit = method.GetCustomAttribute<RateLimitAttribute>() ?? controller.GetCustomAttribute<RateLimitAttribute>();
        Assert.Equal(policy, rateLimit?.PolicyName);
        Assert.Equal(policy, RateLimitPolicyCatalog.GetByName(policy).Name.Value);

        var permissions = method.GetCustomAttributes<HasPermissionAttribute>()
            .Concat(controller.GetCustomAttributes<HasPermissionAttribute>())
            .Select(p => p.Policy);
        Assert.Contains(permissions, p => p?.EndsWith(permission, StringComparison.Ordinal) == true);

        var actors = method.GetCustomAttribute<AllowActorTypesAttribute>() ?? controller.GetCustomAttribute<AllowActorTypesAttribute>();
        Assert.NotNull(actors);
        Assert.Contains(actor, actors!.ActorTypes);
    }

    [Fact]
    public void Every_action_declares_actor_types_and_rate_limit()
    {
        var violations = Controllers()
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .Where(m =>
                (m.GetCustomAttribute<AllowActorTypesAttribute>() ?? m.DeclaringType!.GetCustomAttribute<AllowActorTypesAttribute>()) is null
                || (
                    m.GetCustomAttribute<RateLimitAttribute>() is null
                    && m.GetCustomAttribute<RateLimitExemptAttribute>() is null
                )
            )
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToArray();

        Assert.True(violations.Length == 0, "Acciones sin actor o sin rate limit: " + string.Join(", ", violations));
    }

    [Fact]
    public void Controllers_do_not_touch_persistence_or_http_clients()
    {
        var offenders = Controllers()
            .Where(c =>
                c.GetConstructors()
                    .SelectMany(ctor => ctor.GetParameters())
                    .Any(p =>
                        p.ParameterType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
                        || p.ParameterType.Namespace?.Contains(".Infrastructure", StringComparison.Ordinal) == true
                        || p.ParameterType == typeof(HttpClient)
                        || p.ParameterType == typeof(IHttpClientFactory)
                    )
            )
            .Select(c => c.Name)
            .ToArray();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> Controllers() =>
        ApiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));

    private static MethodInfo FindAction(string qualified)
    {
        var parts = qualified.Split('.');
        var controller = Controllers().Single(c => c.Name == parts[0]);
        return controller.GetMethod(parts[1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"Falta la acción {qualified}.");
    }
}
