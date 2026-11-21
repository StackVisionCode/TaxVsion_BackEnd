using System.Text;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// La declaración que publica Communication (Node) por el fanout: con la propiedad AMQP <c>type</c> = identidad y la
/// carga exacta de su outbox, el host real la resuelve, la deserializa y deja la fila declarada con la fecha de
/// <c>occurredOn</c>.
/// </summary>
public sealed class NodeDeclaredSourceTests(CloudStorageApiFactory factory)
    : IClassFixture<CloudStorageApiFactory>,
        IAsyncLifetime
{
    private const string Identity = "cloudstorage.file_source_declared.v1";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _file = Guid.NewGuid();
    private readonly Guid _conversation = Guid.NewGuid();
    private WebApplicationFactory<Program>? _host;

    public async Task InitializeAsync()
    {
        var at = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);
        var key = ObjectKey.Create($"tenants/{_tenant:N}/communication/files/2026/{_file:N}.pdf").Value;
        var file = FileObject
            .Register(
                _file,
                _tenant,
                OwnerType.Communication,
                _conversation,
                FolderType.Other,
                2026,
                key,
                "W-2.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                at,
                at.AddHours(1)
            )
            .Value;
        file.MarkPendingScan(at);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", at);
        await using var db = NewContext();
        db.Files.Add(file);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenant).ExecuteDeleteAsync();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task The_exact_node_message_declares_the_row_with_its_occurred_on()
    {
        _host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", CloudStorageIntegrationDatabase.ConnectionString)
        );
        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>();
        // Lo que hace el listener: el tipo sale de la propiedad AMQP `type` y el cuerpo, del serializador por defecto.
        Assert.True(_host.Services.GetRequiredService<HandlerGraph>().TryFindMessageType(Identity, out var type));
        Assert.Equal(typeof(FileSourceDeclaredIntegrationEvent), type);
        var json = $$"""
            {
              "eventId": "{{Guid.NewGuid()}}", "eventType": "{{Identity}}",
              "tenantId": "{{_tenant}}", "correlationId": "corr-node",
              "occurredOnUtc": "2026-11-20T15:04:05.000Z", "occurredOn": "2026-11-20T15:04:05.000Z",
              "fileId": "{{_file}}", "sourceService": "communication",
              "sourceResourceType": "conversation", "sourceResourceId": "{{_conversation}}",
              "sourceRole": "attachment", "relatedCustomerId": null
            }
            """;
        var envelope = new Envelope
        {
            Data = Encoding.UTF8.GetBytes(json),
            MessageType = Identity,
            ContentType = "application/json",
        };
        var message = runtime.Options.DefaultSerializer.ReadFromData(type, envelope);

        using (var scope = _host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message);

        await using var db = NewContext();
        var row = await db.Files.IgnoreQueryFilters().AsNoTracking().SingleAsync(f => f.Id == _file);
        Assert.Equal("communication", row.SourceService);
        Assert.Equal("conversation", row.SourceResourceType);
        Assert.Equal(_conversation, row.SourceResourceId);
        Assert.Equal("attachment", row.SourceRole);
        Assert.Null(row.RelatedCustomerId);
        Assert.Equal(new DateTime(2026, 11, 20, 15, 4, 5, DateTimeKind.Utc).Ticks, row.SourceDeclaredAtUtc?.Ticks);
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
