using System.Reflection;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Controllers;
using TaxVision.CloudStorage.Application.Administration;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>D11 del plan único: Subscription pregunta cuánto ocupa una oficina antes de bajarle el almacenamiento.</summary>
public sealed class GetTenantStorageUsedHandlerTests
{
    [Fact]
    public async Task Returns_the_used_and_reserved_bytes_of_the_office()
    {
        var tenantId = Guid.NewGuid();
        var limit = TenantStorageLimit.Create(tenantId, "enterprise", 1_000_000, 1_000_000);
        Assert.True(limit.Reserve(300).IsSuccess);
        limit.Commit(200);
        var limits = new FakeStorageLimitRepository();
        limits.Seed(limit);

        var result = await GetTenantStorageUsedHandler.Handle(new GetTenantStorageUsedQuery(tenantId), limits, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(limit.UsedBytes, result.Value.UsedBytes);
        Assert.Equal(limit.ReservedBytes, result.Value.ReservedBytes);
        Assert.Equal(200, result.Value.UsedBytes);
    }

    // Sin cuota aprovisionada no hay archivos: ocupa cero, no es un error que bloquee la migración.
    [Fact]
    public async Task An_office_without_a_storage_quota_uses_nothing()
    {
        var tenantId = Guid.NewGuid();

        var result = await GetTenantStorageUsedHandler.Handle(
            new GetTenantStorageUsedQuery(tenantId),
            new FakeStorageLimitRepository(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.UsedBytes);
        Assert.Equal(0, result.Value.ReservedBytes);
    }

    [Fact]
    public void The_usage_endpoint_is_internal_and_only_for_service_tokens()
    {
        var controller = typeof(InternalStorageUsageController);
        var action = controller.GetMethod(nameof(InternalStorageUsageController.Get))!;

        Assert.Equal("internal/storage/usage", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal([ActorType.Service], controller.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes);
        // El cliente M2M de Subscription no lleva permisos: el candado es el tipo de actor.
        Assert.Null(action.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Null(controller.GetCustomAttribute<HasPermissionAttribute>());
    }
}
