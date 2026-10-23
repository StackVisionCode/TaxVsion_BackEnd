using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Controllers;

namespace TaxVision.CloudStorage.Tests.Architecture;

public sealed class FormParticipantReassignmentEndpointTests
{
    [Fact]
    public void Endpoint_is_internal_service_only_and_uses_the_dedicated_permission()
    {
        var controller = typeof(InternalFilesController);
        var action = controller.GetMethod(nameof(InternalFilesController.ReassignFormParticipant))!;

        Assert.Equal("internal/files", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            "reassign-form-participant",
            action.GetCustomAttribute<HttpPostAttribute>()!.Template
        );
        Assert.Equal(
            [ActorType.Service],
            controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes
        );
        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + CloudStoragePermissions.ReassignFormParticipant,
            action.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
    }
}
