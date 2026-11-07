extern alias BbTesting;

using System.Reflection;
using BbTesting::TaxVision.BuildingBlocks.Testing.Messaging;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using JasperFx;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minio;
using Minio.DataModel.Response;
using TaxVision.Customer.Application.Imports.Commands.StartCustomerImport;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;
using Wolverine;
using Xunit;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// BB-17. Host real y handlers reales, así que el cliente de almacenamiento se resuelve como en producción. Se mide en
/// lo entregado por una cola local durable. Solo MinIO es de mentira.
/// </summary>
public sealed class CustomerOutboxIntegrationTests : IAsyncLifetime
{
    private readonly CustomerTemporaryDatabase _database = new();
    private readonly DeliveredProbe _delivered = new();
    private readonly HandlerFailureSwitch _failure = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new CustomerApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _database.ConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(_delivered);
                services.AddSingleton(_failure);
                services.RemoveAll<IMinioClient>();
                services.AddSingleton(AcceptingMinio.Create());
                services.ConfigureWolverine(options =>
                {
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Discovery.IncludeType(typeof(CustomerOutboxProbeHandler));
                    options.Policies.AddMiddleware(
                        typeof(FailAfterHandlerMiddleware),
                        chain =>
                            chain.MessageType == typeof(StartCustomerImportCommand)
                            || chain.MessageType == typeof(CustomerCreationFromFormRequestedIntegrationEvent)
                    );
                    options
                        .PublishMessage<SaveFileRequestedIntegrationEvent>()
                        .ToLocalQueue("bb17-probe")
                        .UseDurableInbox();
                    options
                        .PublishMessage<CustomerCreationFromFormRejectedIntegrationEvent>()
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
        await _database.DisposeAsync();
    }

    private async Task InvokeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CustomerImportAttemptResponse>>(
                new StartCustomerImportCommand(
                    _tenantId,
                    Guid.NewGuid(),
                    "bb-17",
                    DuplicateStrategy.Skip,
                    ImportSourceKind.Csv,
                    "customers.csv",
                    [1, 2, 3]
                )
            );
    }

    private async Task<int> AttemptsAsync()
    {
        await using var db = _database.NewContext(_tenantId);
        return await db.Set<CustomerImportAttempt>().IgnoreQueryFilters().CountAsync(a => a.TenantId == _tenantId);
    }

    [Fact(Timeout = 120_000)]
    public async Task Failure_after_the_upload_delivers_nothing()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(InvokeAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(0, await AttemptsAsync());
        Assert.Empty(_delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_save_request()
    {
        await InvokeAsync();
        await _delivered.SettleAsync(expected: 1);

        Assert.Equal(1, await AttemptsAsync());
        Assert.Single(_delivered.Of<SaveFileRequestedIntegrationEvent>());
    }

    // CreationContext lo arma el consumer con el bus que recibe por parámetro: no sale del contenedor.
    [Fact(Timeout = 120_000)]
    public async Task Failure_after_rejecting_a_form_delivers_nothing()
    {
        _failure.Armed = true;

        await Assert.ThrowsAsync<ArgumentException>(RejectFormAsync);
        await _delivered.SettleAsync(expected: 1);

        Assert.Empty(_delivered.Of<CustomerCreationFromFormRejectedIntegrationEvent>());
    }

    [Fact(Timeout = 120_000)]
    public async Task Success_delivers_the_form_rejection()
    {
        await RejectFormAsync();
        await _delivered.SettleAsync(expected: 1);

        var rejected = Assert.Single(_delivered.Of<CustomerCreationFromFormRejectedIntegrationEvent>());
        Assert.Equal(CustomerCreationFromFormOutcomes.CreatorNotInDirectory, rejected.ErrorCode);
    }

    // Quien pide el alta no está en el directorio: el consumer rechaza sin escribir nada.
    private async Task RejectFormAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var sourceReferenceId = Guid.NewGuid();
        var participant = new ClientFormParticipant { Kind = "External", ParticipantId = Guid.NewGuid() };
        var idempotencyKey = $"bb17:{sourceReferenceId:N}";
        await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync(
                new CustomerCreationFromFormRequestedIntegrationEvent
                {
                    TenantId = _tenantId,
                    CorrelationId = "bb-17",
                    RequestId = Guid.NewGuid(),
                    IdempotencyKey = idempotencyKey,
                    SourceService = CustomerCreationFromFormOutcomes.ClientFormsSourceService,
                    SourceReferenceId = sourceReferenceId,
                    RevisionNumber = 1,
                    RequestedByUserId = Guid.NewGuid(),
                    TargetCatalogVersion = 1,
                    Participant = participant,
                    Payload = scope
                        .ServiceProvider.GetRequiredService<IProtectedPayloadCodec>()
                        .Protect(
                            ClientFormProtectedPayloadSchemas.CustomerCreation,
                            new ProtectedPayloadBinding(
                                _tenantId,
                                sourceReferenceId,
                                participant,
                                IdempotencyKey: idempotencyKey
                            ),
                            new CustomerCreationFromFormPayload([])
                        ),
                }
            );
    }

    public static class CustomerOutboxProbeHandler
    {
        public static void Handle(SaveFileRequestedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);

        public static void Handle(CustomerCreationFromFormRejectedIntegrationEvent message, DeliveredProbe probe) =>
            probe.Add(message);
    }

    public class AcceptingMinio : DispatchProxy
    {
        public static IMinioClient Create() => Create<IMinioClient, AcceptingMinio>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IMinioClient.PutObjectAsync)
                ? Task.FromResult<PutObjectResponse>(null!)
                : null;
    }
}
