using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Customer.Application.Customers.Avatars;
using TaxVision.Customer.Application.Customers.Portal;
using Wolverine;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// Cableado real de la foto de cliente: Wolverine genera el código del handler al primer mensaje, así que
/// una dependencia sin registrar (normalizador, almacenamiento, TimeProvider) solo explota en vivo. Aquí
/// cada handler/consumer nuevo se invoca una vez por el bus del host real contra un cliente inexistente:
/// no escribe nada en la base y los transportes externos van anulados.
/// </summary>
public sealed class CustomerAvatarWiringTests : IClassFixture<CustomerApiFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CustomerAvatarWiringTests(CustomerApiFactory baseFactory) =>
        factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                foreach (
                    var job in services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && descriptor.ImplementationType?.Namespace?.StartsWith(
                                "TaxVision.",
                                StringComparison.Ordinal
                            ) == true
                        )
                        .ToList()
                )
                    services.Remove(job);
            })
        );

    [Fact]
    public async Task Avatar_handlers_resolve_their_dependencies_through_the_bus()
    {
        var tenantId = Guid.NewGuid();
        var missingCustomer = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var contact = await bus.InvokeAsync<Result<MyCustomerContactResponse>>(
            new GetMyCustomerContactQuery(tenantId, missingCustomer)
        );
        var upload = await bus.InvokeAsync<Result<UploadCustomerAvatarResponse>>(
            new UploadCustomerAvatarCommand(
                tenantId,
                missingCustomer,
                Guid.NewGuid(),
                CustomerAvatarCaller.PortalSelf,
                false,
                [1, 2, 3]
            )
        );
        var remove = await bus.InvokeAsync<Result>(
            new RemoveCustomerAvatarCommand(tenantId, missingCustomer, Guid.NewGuid(), CustomerAvatarCaller.Staff, true)
        );

        Assert.Equal("Customer.NotFound", contact.Error.Code);
        Assert.Equal("Customer.NotFound", upload.Error.Code);
        Assert.Equal("Customer.NotFound", remove.Error.Code);
    }

    [Fact]
    public async Task Scan_result_consumers_run_alongside_the_import_consumer()
    {
        var tenantId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(
            new FileAvailableIntegrationEvent
            {
                TenantId = tenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "k",
                ContentType = "image/webp",
                SizeBytes = 1,
                ChecksumSha256 = "x",
                CreatedBy = Guid.NewGuid(),
                OwnerType = "Customer",
                OwnerId = Guid.NewGuid(),
                FolderType = "Avatars",
            }
        );
        await bus.InvokeAsync(
            new FileScanFailedIntegrationEvent
            {
                TenantId = tenantId,
                FileId = Guid.NewGuid(),
                ObjectKey = "k",
                Reason = "test",
                OwnerType = "Customer",
                OwnerId = Guid.NewGuid(),
                FolderType = "Avatars",
            }
        );
    }
}
