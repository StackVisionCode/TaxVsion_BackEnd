using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using TaxVision.Signature.Api.Controllers;

namespace TaxVision.Signature.Tests.Architecture;

/// <summary>
/// El Gateway solo enruta /signature/**: las rutas de plataforma por oficina deben vivir bajo ese
/// prefijo, y los techos de una oficina se tienen que poder leer, no solo escribir.
/// </summary>
public sealed class SignatureAdminControllerTests
{
    [Fact]
    public void Office_routes_live_under_the_signature_prefix()
    {
        var route = typeof(SignatureAdminController).GetCustomAttribute<RouteAttribute>();

        Assert.NotNull(route);
        Assert.StartsWith("signature/", route.Template, StringComparison.Ordinal);
    }

    [Fact]
    public void Signature_constraints_of_an_office_can_be_read()
    {
        var reads = typeof(SignatureAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
            .Select(attribute => attribute.Template);

        Assert.Contains("signature-constraints", reads);
    }
}
