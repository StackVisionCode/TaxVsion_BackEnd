using System.Diagnostics.Metrics;
using System.Security.Claims;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Domain;
using BuildingBlocks.ResourceAuthorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.ResourceAuthorization;

/// <summary>
/// RBAC Fase 4 (RBAC_Hardening_Plan.md) — Layer 3b (resource ownership). Función pura
/// (<see cref="AuthorizationHandler{TRequirement,TResource}"/> sin infraestructura), testeada
/// invocando <see cref="IAuthorizationHandler.HandleAsync"/> directamente sobre un
/// <see cref="AuthorizationHandlerContext"/> construido a mano — sin DI, sin TestServer, mismo
/// criterio de "sin precedente de WebApplicationFactory en el repo" que ya siguen
/// Connectors.Tests/Customer.Tests para policies.
/// </summary>
[Collection(TaxVision.BuildingBlocks.Tests.ActorTypeAuthorization.AuthorizationMetricsCollection.Name)]
public sealed class IsOwnerOrHasManageHandlerTests
{
    private sealed class TestResource : IHasOwner
    {
        public required Guid CreatedByUserId { get; init; }
    }

    private const string ManagePermission = "cloudstorage.share.manage";

    private static ClaimsPrincipal User(params Claim[] claims) => new(new ClaimsIdentity(claims, "Test"));

    private static async Task<bool> AuthorizeAsync(
        ClaimsPrincipal user,
        TestResource resource,
        string? managePermission = null
    )
    {
        var handler = new IsOwnerOrHasManageHandler<TestResource>(
            managePermission,
            new JwtEmbeddedPermissionsSource(),
            new AuthorizationMetrics()
        );
        var context = new AuthorizationHandlerContext([Operations.Revoke], user, resource);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Owner_can_operate_on_their_own_resource()
    {
        var ownerId = Guid.NewGuid();
        var resource = new TestResource { CreatedByUserId = ownerId };
        var user = User(new Claim("sub", ownerId.ToString()));

        Assert.True(await AuthorizeAsync(user, resource));
    }

    [Fact]
    public async Task Non_owner_without_manage_permission_cannot_operate_on_another_users_resource()
    {
        var resource = new TestResource { CreatedByUserId = Guid.NewGuid() };
        var user = User(new Claim("sub", Guid.NewGuid().ToString()));

        Assert.False(await AuthorizeAsync(user, resource));
    }

    [Fact]
    public async Task User_with_the_manage_permission_can_operate_on_another_users_resource()
    {
        var resource = new TestResource { CreatedByUserId = Guid.NewGuid() };
        var user = User(new Claim("sub", Guid.NewGuid().ToString()), new Claim("perm", ManagePermission));

        Assert.True(await AuthorizeAsync(user, resource, ManagePermission));
    }

    [Fact]
    public async Task PlatformAdmin_can_always_operate_regardless_of_ownership()
    {
        var resource = new TestResource { CreatedByUserId = Guid.NewGuid() };
        var user = User(new Claim("sub", Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, "PlatformAdmin"));

        Assert.True(await AuthorizeAsync(user, resource));
    }

    /// <summary>RBAC Fase 10 — authz_decision_metric_incremented_on_deny.</summary>
    [Fact]
    public async Task Denying_ownership_increments_the_authz_decision_metric_with_layer_3b()
    {
        var measurements = new List<(long Value, string? Result, string? Layer)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AuthorizationMetrics.MeterName && instrument.Name == "authz.decision")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
            {
                string? result = null;
                string? layer = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "result")
                        result = tag.Value?.ToString();
                    else if (tag.Key == "layer")
                        layer = tag.Value?.ToString();
                }
                measurements.Add((measurement, result, layer));
            }
        );
        listener.Start();

        var resource = new TestResource { CreatedByUserId = Guid.NewGuid() };
        var user = User(new Claim("sub", Guid.NewGuid().ToString()));

        Assert.False(await AuthorizeAsync(user, resource));

        var measurement = Assert.Single(measurements);
        Assert.Equal("deny", measurement.Result);
        Assert.Equal("3b", measurement.Layer);
    }
}
