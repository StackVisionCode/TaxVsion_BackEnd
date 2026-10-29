using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.Api.Controllers.Portal.ClientForms;
using TaxVision.Tasks.Api.Filters;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsAccessFilterTests
{
    private static readonly string[] GatedPrefixes = ["tasks/forms", "tasks/portal/forms"];
    private const string PublicPrefix = "tasks/public/forms";

    // Las únicas acciones que pueden saltarse la disponibilidad: descubrimiento, operación de plataforma y apagado.
    private static readonly HashSet<string> SkippedActions =
    [
        "FormAvailabilityController.Get",
        "PortalFormAvailabilityController.Get",
        "FormPlatformController.List",
        "FormPlatformController.Set",
        "FormLibraryPlatformController.RecordReview",
        "FormLibraryPlatformController.SetListing",
        "FormLibraryPlatformController.Export",
        "FormCatalogUsageController.Get",
        "FormIntakeLinksController.Pause",
        "FormIntakeLinksController.Revoke",
    ];

    [Fact]
    public void Template_controller_requires_feature_access()
    {
        Assert.True(
            ClientFormsAccessFilter.RequiresClientFormsAccess(
                Descriptor<FormTemplatesController>(nameof(FormTemplatesController.Get))
            )
        );
    }

    [Theory]
    [InlineData(typeof(FormAvailabilityController), nameof(FormAvailabilityController.Get))]
    [InlineData(typeof(PortalFormAvailabilityController), nameof(PortalFormAvailabilityController.Get))]
    [InlineData(typeof(FormPlatformController), nameof(FormPlatformController.List))]
    public void Recovery_and_discovery_endpoints_bypass_feature_access(Type controller, string action)
    {
        var descriptor = new ControllerActionDescriptor
        {
            ControllerTypeInfo = controller.GetTypeInfo(),
            MethodInfo = controller.GetMethod(action)!,
        };

        Assert.False(ClientFormsAccessFilter.RequiresClientFormsAccess(descriptor));
    }

    [Fact]
    public void Gate_does_not_depend_on_the_namespace()
    {
        Assert.False(
            ClientFormsAccessFilter.RequiresClientFormsAccess(
                Descriptor<UnmarkedClientFormsController>(nameof(UnmarkedClientFormsController.Get))
            )
        );
    }

    [Fact]
    public void Every_client_forms_route_is_gated_unless_explicitly_skipped()
    {
        var violations = new List<string>();
        foreach (var (descriptor, route) in Actions())
        {
            var name = $"{descriptor.ControllerTypeInfo.Name}.{descriptor.MethodInfo.Name}";
            if (route.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsMarked(descriptor) || !IsSkipped(descriptor))
                    violations.Add($"{name} ({route}) is public and must be marked and explicitly skipped");
                continue;
            }
            if (!GatedPrefixes.Any(prefix => route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!IsMarked(descriptor))
                violations.Add($"{name} ({route}) lacks [ClientFormsEndpoint]");
            else if (IsSkipped(descriptor) != SkippedActions.Contains(name))
                violations.Add($"{name} ({route}) skip does not match the allowlist");
            else if (!IsSkipped(descriptor) && !ClientFormsAccessFilter.RequiresClientFormsAccess(descriptor))
                violations.Add($"{name} ({route}) is not gated");
        }

        Assert.True(violations.Count == 0, string.Join(" | ", violations));
    }

    private static IEnumerable<(ControllerActionDescriptor Descriptor, string Route)> Actions() =>
        typeof(Program)
            .Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type =>
                type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
                    .Select(method =>
                        (
                            new ControllerActionDescriptor
                            {
                                ControllerTypeInfo = type.GetTypeInfo(),
                                MethodInfo = method,
                            },
                            Route(type, method)
                        )
                    )
            );

    private static string Route(Type controller, MethodInfo method)
    {
        var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
        var template = method.GetCustomAttributes<HttpMethodAttribute>().First().Template;
        if (string.IsNullOrEmpty(template))
            return prefix;
        return template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)
            ? template.TrimStart('~', '/')
            : $"{prefix}/{template}";
    }

    private static bool IsMarked(ControllerActionDescriptor descriptor) =>
        descriptor.ControllerTypeInfo.IsDefined(typeof(ClientFormsEndpointAttribute), true);

    private static bool IsSkipped(ControllerActionDescriptor descriptor) =>
        descriptor.MethodInfo.IsDefined(typeof(SkipClientFormsAccessAttribute), true)
        || descriptor.ControllerTypeInfo.IsDefined(typeof(SkipClientFormsAccessAttribute), true);

    private static ControllerActionDescriptor Descriptor<T>(string action) =>
        new() { ControllerTypeInfo = typeof(T).GetTypeInfo(), MethodInfo = typeof(T).GetMethod(action)! };

    // Mismo namespace que los de Client Forms pero sin la marca: el filtro no debe adivinar por el nombre.
    private sealed class UnmarkedClientFormsController : ControllerBase
    {
        public IActionResult Get() => Ok();
    }
}
