extern alias BbTesting;

using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// BB-17. Host real y handler real, así que el cliente de almacenamiento se resuelve como en producción. Se mide en lo
/// entregado por una cola local durable. El archivo ya está "en MinIO": este paso no lo toca.
/// </summary>
public sealed class TenantBrandOutboxIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private readonly DeliveredProbe _delivered = new();
    private readonly HandlerFailureSwitch _failure = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _factory = new TenantApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_delivered);
                services.AddSingleton(_failure);
                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(SaveFileProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain => chain.MessageType == typeof(SaveTenantBrandAssetCommand)
                    );
                    options
                        .PublishMessage<SaveFileRequestedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                });
            });
        });
        _ = _factory.Services;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DropAsync();
    }

    private async Task InvokeAsync()
    {
        var fileId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<UploadTenantBrandAssetResponse>>(
                new SaveTenantBrandAssetCommand(
                    _tenantId,
                    BrandSurface.Crm,
                    BrandAssetKey.Logo,
                    new TenantLogoUpload([1, 2, 3], "image/png", "logo.png", Guid.NewGuid()),
                    new TenantBrandStoredFile(fileId, $"tenant-branding/{fileId:N}/logo.png")
                )
            );
    }

    private async Task<int> BrandsAsync()
    {
        await using var db = _database.NewContext();
        return await db.TenantBrands.IgnoreQueryFilters().CountAsync(brand => brand.TenantId == _tenantId);
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_requesting_the_catalog_delivers_nothing()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(InvokeAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await BrandsAsync());
        Assert.Empty(_delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_catalog_request()
    {
        await InvokeAsync();
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(1, await BrandsAsync());
        Assert.Single(_delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    public static class SaveFileProbeHandler
    {
        public static void Handle(SaveFileRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);
    }
}
