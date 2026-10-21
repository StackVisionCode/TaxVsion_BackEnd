using System.Reflection;
using Microsoft.AspNetCore.Mvc.Controllers;
using TaxVision.Tasks.Api.Controllers.ClientForms;
using TaxVision.Tasks.Api.Controllers.Portal.ClientForms;
using TaxVision.Tasks.Api.Filters;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ClientFormsAccessFilterTests
{
    [Fact]
    public void Template_controller_requires_feature_access()
    {
        Assert.True(ClientFormsAccessFilter.RequiresClientFormsAccess(Descriptor<FormTemplatesController>(nameof(FormTemplatesController.Get))));
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

    private static ControllerActionDescriptor Descriptor<T>(string action) =>
        new()
        {
            ControllerTypeInfo = typeof(T).GetTypeInfo(),
            MethodInfo = typeof(T).GetMethod(action)!,
        };
}
