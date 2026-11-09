using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Infrastructure.Jobs;

/// <summary>
/// COR-18 (D-COR-5): borra el correo de quien no es cliente pasado el período, con sus copias y adjuntos. Una unidad
/// por correo. Nunca toca lo enlazado a un cliente ni lo enviado. Apagado hasta que el dueño lo encienda.
/// </summary>
public sealed class NotAClientMailRetentionJob(
    IServiceScopeFactory scopeFactory,
    IOptions<NotAClientMailRetentionOptions> options,
    ILogger<NotAClientMailRetentionJob> logger
) : BackgroundService
{
    private const string SourceService = "correspondence.not_a_client_retention";
    private const string DeletionReason = "retention";
    private const string UserOwner = "User";
    private const string IncomingFolder = "EmailIncoming";
    private const string FileGone = "CloudStorageClient.FileNotRegistered";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        await using (var scope = scopeFactory.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<IHostApplicationLifetime>()
                .WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "NotAClientMailRetentionJob iteration failed.");
            }

            try
            {
                await Task.Delay(settings.Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            return;

        // El job es el origen de la traza: un id por pasada.
        var correlationId = Guid.NewGuid().ToString("N");
        var cutoffUtc = DateTime.UtcNow.AddDays(-settings.RetentionDays);
        var deleted = 0;
        var pending = 0;

        for (var batch = 0; batch < settings.MaxBatchesPerRun && !ct.IsCancellationRequested; batch++)
        {
            // Lo que no se pudo borrar sigue en la consulta: se salta para no repetirlo en esta pasada.
            var candidates = await ListExpiredAsync(cutoffUtc, pending, settings.BatchSize, ct);
            foreach (var emailId in candidates)
            {
                if (await ExpireAsync(emailId, cutoffUtc, correlationId, ct))
                    deleted++;
                else
                    pending++;
            }

            if (candidates.Count < settings.BatchSize)
                break;
        }

        if (deleted > 0 || pending > 0)
            logger.LogInformation(
                "NotAClientMailRetentionJob deleted {Deleted} message(s) older than {Cutoff}; {Pending} left for the next run.",
                deleted,
                cutoffUtc,
                pending
            );
    }

    // Cruza tenants, igual que los demás barridos. El hilo con cliente queda fuera: ese correo ya tiene dueño.
    private async Task<IReadOnlyList<Guid>> ListExpiredAsync(
        DateTime cutoffUtc,
        int skip,
        int take,
        CancellationToken ct
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CorrespondenceDbContext>();
        return await db
            .IncomingEmails.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(email =>
                email.CustomerId == null
                && email.ReceivedAtUtc < cutoffUtc
                && !db
                    .EmailThreads.IgnoreQueryFilters()
                    .Any(thread => thread.Id == email.EmailThreadId && thread.CustomerId != null)
            )
            .OrderBy(email => email.ReceivedAtUtc)
            .ThenBy(email => email.Id)
            .Select(email => email.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    private async Task<bool> ExpireAsync(Guid emailId, DateTime cutoffUtc, string correlationId, CancellationToken ct)
    {
        // Fuera de la unidad: es una llamada a otro servicio. Sin respuesta, el correo espera la pasada siguiente.
        var files = await ResolveFilesAsync(emailId, ct);
        if (files is null)
            return false;

        var result = await TransactionalJobUnit.RunAsync<CorrespondenceDbContext>(
            scopeFactory,
            (unit, token) => DeleteAsync(unit, emailId, files, cutoffUtc, correlationId, token),
            logger,
            ct
        );
        return result.Outcome == JobUnitOutcome.Committed;
    }

    /// <summary>Lo que pedirle borrar a CloudStorage. Null si no se pudo saber de quién es algún archivo.</summary>
    private async Task<IReadOnlyList<FileDeletionTarget>?> ResolveFilesAsync(Guid emailId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CorrespondenceDbContext>();
        var attachments = await db
            .IncomingEmailAttachments.AsNoTracking()
            .Where(attachment => attachment.IncomingEmailId == emailId && attachment.CloudStorageFileId != null)
            .Select(attachment => new { attachment.TenantId, FileId = attachment.CloudStorageFileId!.Value })
            .ToListAsync(ct);
        if (attachments.Count == 0)
            return [];

        var storage = scope.ServiceProvider.GetRequiredService<ICloudStorageClient>();
        var targets = new List<FileDeletionTarget>();
        foreach (var attachment in attachments)
        {
            var file = await storage.GetFileMetadataAsync(attachment.TenantId, attachment.FileId, ct);
            if (file.IsFailure)
            {
                if (file.Error.Code == FileGone)
                    continue;
                logger.LogWarning(
                    "Could not read file {FileId} ({Code}); mail kept.",
                    attachment.FileId,
                    file.Error.Code
                );
                return null;
            }

            // Si alguien lo pasó a un cliente, ese archivo ya no depende de este correo.
            if (file.Value is { OwnerType: UserOwner, FolderType: IncomingFolder, OwnerId: { } ownerId })
                targets.Add(
                    new FileDeletionTarget
                    {
                        FileId = attachment.FileId,
                        ExpectedOwnerType = UserOwner,
                        ExpectedOwnerId = ownerId,
                        ExpectedFolderType = IncomingFolder,
                    }
                );
        }

        return targets;
    }

    private static async Task<bool> DeleteAsync(
        JobUnitContext<CorrespondenceDbContext> unit,
        Guid emailId,
        IReadOnlyList<FileDeletionTarget> files,
        DateTime cutoffUtc,
        string correlationId,
        CancellationToken ct
    )
    {
        var db = unit.DbContext;
        var email = await db
            .IncomingEmails.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.Id == emailId)
            .Select(e => new
            {
                e.TenantId,
                e.EmailThreadId,
                e.DeletedAtUtc,
            })
            .FirstOrDefaultAsync(ct);
        if (email is null)
            return false;

        var thread = await db
            .EmailThreads.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == email.EmailThreadId, ct);
        if (thread?.CustomerId is not null)
            return false;

        // La condición va en el DELETE: un enlace que llegue en este instante gana y aquí no se borra nada.
        var removed = await db
            .IncomingEmails.IgnoreQueryFilters()
            .Where(e => e.Id == emailId && e.CustomerId == null && e.ReceivedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);
        if (removed == 0)
            return false;

        if (thread is not null)
        {
            // MessageCount solo cuenta entrantes vivos: el de la papelera ya se descontó.
            if (email.DeletedAtUtc is null)
                thread.DecrementMessageCount();
            await CloseThreadIfEmptyAsync(db, thread, ct);
        }

        if (files.Count > 0)
            await unit.PublishAsync(
                new FilesDeletionRequestedIntegrationEvent
                {
                    TenantId = email.TenantId,
                    CorrelationId = correlationId,
                    SourceService = SourceService,
                    SourceReferenceId = emailId,
                    Reason = DeletionReason,
                    Files = files,
                }
            );

        return true;
    }

    // Un hilo sin entrantes y con lo enviado se archiva: si no, caería en el Inbox como un aviso de no entrega.
    private static async Task CloseThreadIfEmptyAsync(
        CorrespondenceDbContext db,
        EmailThread thread,
        CancellationToken ct
    )
    {
        var hasOtherContent =
            await db.IncomingEmails.IgnoreQueryFilters().AnyAsync(e => e.EmailThreadId == thread.Id, ct)
            || await db.Drafts.IgnoreQueryFilters().AnyAsync(d => d.EmailThreadId == thread.Id, ct)
            || await db.DeliveryNotices.IgnoreQueryFilters().AnyAsync(n => n.EmailThreadId == thread.Id, ct);

        if (!hasOtherContent)
            db.EmailThreads.Remove(thread);
        else if (thread.MessageCount == 0)
            thread.Archive();
    }
}
