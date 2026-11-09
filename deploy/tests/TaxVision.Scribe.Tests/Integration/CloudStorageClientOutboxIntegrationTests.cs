using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Application.SystemAssets.Commands;
using TaxVision.Scribe.Application.Templates.Storage;
using TaxVision.Scribe.Domain.Projections;
using Wolverine;

namespace TaxVision.Scribe.Tests.Integration;

/// <summary>
/// SCR-07 contra SQL real, con la configuración de Wolverine de Program.cs. El <c>IMessageBus</c> de
/// <c>CloudStorageClient</c> sale del contenedor (cliente HTTP tipado), no es el contexto del handler, así que
/// no entra en su transacción: <c>SaveFileRequested</c> sale antes del commit. De eso depende el polling del
/// logo. La prueba de control muestra que el bus del handler sí espera al commit.
/// </summary>
public sealed class CloudStorageClientOutboxIntegrationTests(ScribeOutboxFixture fixture)
    : IClassFixture<ScribeOutboxFixture>
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    [Fact]
    public async Task SaveFileRequested_is_sent_before_handler_commit()
    {
        // CloudStorage (el doble) solo deja descargar lo que ya recibió: el handler real recorre su polling
        // (SCR-08: fuera de la transacción) y únicamente sale bien si el mensaje ya se fue.
        var result = await InvokeAsync(bus =>
            bus.InvokeAsync<Result<UpdateSystemHeaderLogoResult>>(
                new UpdateSystemHeaderLogoCommand(Png, "image/png", Guid.NewGuid())
            )
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, await fixture.CountHeaderLogosAsync(result.Value.FileId));
    }

    [Fact]
    public async Task Rollback_after_upload_keeps_the_sent_message_and_loses_the_row()
    {
        var command = new UploadThenFailCommand();

        await Assert.ThrowsAsync<ArgumentException>(() => InvokeAsync(bus => bus.InvokeAsync(command)));

        // Peor caso: blob huérfano. El archivo existe y ninguna fila lo apunta.
        Assert.True(await fixture.Probe.WaitForAsync(command.FileId.Task.Result, TimeSpan.FromSeconds(10)));
        Assert.Equal(0, await fixture.CountHeaderLogosAsync(command.FileId.Task.Result));
    }

    [Fact]
    public async Task Control_message_from_the_handler_context_waits_for_the_commit()
    {
        var command = new PublishThroughHandlerContextCommand(Guid.NewGuid());

        await InvokeAsync(bus => bus.InvokeAsync(command));

        Assert.False(command.DeliveredBeforeCommit.Task.Result);
        Assert.True(await fixture.Probe.WaitForAsync(command.FileId, TimeSpan.FromSeconds(10)));
    }

    private async Task<T> InvokeAsync<T>(Func<IMessageBus, Task<T>> action)
    {
        using var scope = fixture.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMessageBus>());
    }

    private async Task InvokeAsync(Func<IMessageBus, Task> action)
    {
        using var scope = fixture.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<IMessageBus>());
    }
}

public sealed record UploadThenFailCommand
{
    public TaskCompletionSource<Guid> FileId { get; } = new();
}

public sealed record PublishThroughHandlerContextCommand(Guid FileId)
{
    public TaskCompletionSource<bool> DeliveredBeforeCommit { get; } = new();
}

public static class OutboxProbeHandlers
{
    public static class UploadThenFailHandler
    {
        public static async Task Handle(
            UploadThenFailCommand command,
            ITemplateStorageService storage,
            ISystemAssetRefRepository assets,
            IUnitOfWork unitOfWork,
            CancellationToken ct
        )
        {
            var upload = await storage.UploadAsync(null, TemplateArtifactKind.SystemLogo, [1], PlatformTenant.Id, ct);
            command.FileId.TrySetResult(upload.Value.FileId);

            await PointHeaderLogoAsync(assets, upload.Value.FileId, ct);
            await unitOfWork.SaveChangesAsync(ct);

            // Permanente para la política estándar: un solo intento.
            throw new ArgumentException("Forced failure after saving.");
        }
    }

    public static class PublishThroughHandlerContextHandler
    {
        public static async Task Handle(
            PublishThroughHandlerContextCommand command,
            IMessageBus bus,
            ISystemAssetRefRepository assets,
            IUnitOfWork unitOfWork,
            SaveFileProbe probe,
            CancellationToken ct
        )
        {
            await bus.PublishAsync(
                new SaveFileRequestedIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    FileId = command.FileId,
                    RequestingService = ScribeStorageIdentity.RequestingService,
                    SourceBucket = "probe",
                    SourceObjectKey = "probe",
                    ActorId = PlatformTenant.Id,
                    OwnerType = "Tenant",
                    FolderType = "Templates",
                    OriginalName = "probe.png",
                    ContentType = "image/png",
                    SizeBytes = 1,
                }
            );

            command.DeliveredBeforeCommit.TrySetResult(
                await probe.WaitForAsync(command.FileId, TimeSpan.FromSeconds(3))
            );

            await PointHeaderLogoAsync(assets, command.FileId, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static class SaveFileRequestedProbeHandler
    {
        public static void Handle(SaveFileRequestedIntegrationEvent evt, SaveFileProbe probe) =>
            probe.Record(evt.FileId);
    }

    private static async Task PointHeaderLogoAsync(ISystemAssetRefRepository assets, Guid fileId, CancellationToken ct)
    {
        var existing = await assets.GetByKeyAsync(SystemAssetKeys.HeaderLogo, ct);
        if (existing is not null)
            existing.Update(fileId, "image/png", 1, DateTime.UtcNow);
        else
            await assets.AddAsync(
                SystemAssetRef.Create(SystemAssetKeys.HeaderLogo, fileId, "image/png", 1, DateTime.UtcNow),
                ct
            );
    }
}
