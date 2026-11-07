using BuildingBlocks.Caching;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Infrastructure.Persistence.Repositories;
using TaxVision.Tenant.Tests.TestSupport;
using Wolverine;
using Wolverine.Transports.Tcp;
using DomainTenant = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>TEN-7 contra SQL real y el pipeline de Wolverine. Base temporal propia.</summary>
public sealed class AdministrativeHttpOutsideTransactionIntegrationTests : IAsyncLifetime
{
    private readonly TenantTemporaryDatabase _database = new();
    private readonly SaveConflictInterceptor _conflict = new();
    private readonly FakeBillingIssuerProfileClient _billing = new();
    private readonly CountingBrandStorage _storage = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await TenantWolverineHost.StartAsync(
                _database.ConnectionString,
                (options, closedPort) =>
                {
                    options
                        .Discovery.IncludeType(typeof(ImportOfficeProfilesFromBillingHandler))
                        .IncludeType(typeof(ImportOfficeProfilesPageHandler))
                        .IncludeType(typeof(UploadTenantBrandAssetHandler))
                        .IncludeType(typeof(SaveTenantBrandAssetHandler));
                    options.Services.AddScoped<IOfficeProfileRepository, OfficeProfileRepository>();
                    options.Services.AddScoped<ITenantBrandRepository, TenantBrandRepository>();
                    options.Services.AddSingleton<IBillingIssuerProfileClient>(_billing);
                    options.Services.AddSingleton<ITenantBrandingCloudStorageClient>(_storage);
                    options
                        .PublishMessage<TenantProfileChangedIntegrationEvent>()
                        .ToPort(closedPort)
                        .UseDurableOutbox();
                    options.UseTaxVisionConflictPolicy();
                    options.ApplyStandardFailurePolicies();
                },
                _conflict
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        await TenantWolverineHost.StopAsync(_host);
        await _database.DropAsync();
    }

    private IMessageBus Bus => _host!.Services.GetRequiredService<IMessageBus>();

    [Fact(Timeout = 120_000)]
    public async Task Import_ConflictOnPage2_DoesNotRollbackPage1()
    {
        var pageSize = ImportOfficeProfilesFromBillingHandler.PageSize;
        await SeedOfficesAsync(pageSize + 1);
        // La página 2 trae un solo perfil: es el guardado que choca, en todos los intentos.
        _conflict.FailWhen = db =>
            db.ChangeTracker.Entries<OfficeProfile>().Count(e => e.State == EntityState.Added) == 1;

        await Assert.ThrowsAsync<ConflictException>(() =>
            Bus.InvokeAsync<Result<ImportOfficeProfilesResponse>>(
                new ImportOfficeProfilesFromBillingCommand(Guid.NewGuid())
            )
        );

        await using var read = _database.NewContext();
        Assert.Equal(pageSize, await read.Set<OfficeProfile>().IgnoreQueryFilters().CountAsync());
    }

    [Fact(Timeout = 120_000)]
    public async Task Upload_SaveConflict_DoesNotUploadTheFileAgain()
    {
        _conflict.FailOnce = true;

        // BB-11: el conflicto sale como 409; el archivo ya subido queda sin referencia hasta el siguiente intento.
        await Assert.ThrowsAsync<ConflictException>(() =>
            Bus.InvokeAsync<Result<UploadTenantBrandAssetResponse>>(
                new UploadTenantBrandAssetCommand(
                    Guid.NewGuid(),
                    BrandSurface.Crm,
                    BrandAssetKey.Logo,
                    Guid.NewGuid(),
                    [1, 2, 3],
                    "image/png",
                    "logo.png"
                )
            )
        );

        Assert.Equal(1, _storage.Stored);
    }

    private async Task SeedOfficesAsync(int count)
    {
        await using var db = _database.NewContext();
        for (var i = 0; i < count; i++)
        {
            var tenant = DomainTenant.Create($"Office {i}", $"office-{i}", "Etc/UTC", DateTime.UtcNow).Value;
            db.Tenants.Add(tenant);
            _billing.Items.Add(
                new BillingIssuerProfile(
                    tenant.Id,
                    $"Office {i}",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "US",
                    null,
                    null,
                    null,
                    DateTime.UtcNow
                )
            );
        }
        await db.SaveChangesAsync();
    }

    private sealed class SaveConflictInterceptor : SaveChangesInterceptor
    {
        public Func<DbContext, bool>? FailWhen { get; set; }
        public bool FailOnce { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (FailWhen?.Invoke(eventData.Context!) == true || FailOnce)
            {
                FailOnce = false;
                throw new ConflictException("Persistence.ConcurrencyConflict", "Conflict.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CountingBrandStorage : ITenantBrandingCloudStorageClient
    {
        private int _stored;

        public int Stored => _stored;

        public Task<Result<TenantBrandStoredFile>> StoreAsync(
            Guid tenantId,
            TenantLogoUpload upload,
            CancellationToken ct = default
        )
        {
            Interlocked.Increment(ref _stored);
            return Task.FromResult(Result.Success(new TenantBrandStoredFile(Guid.NewGuid(), "key")));
        }

        public Task RequestCatalogAsync(
            Guid tenantId,
            TenantLogoUpload upload,
            TenantBrandStoredFile stored,
            Wolverine.IMessageBus bus,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task<Result<TenantLogoDownloadUrl>> GetDownloadUrlAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
