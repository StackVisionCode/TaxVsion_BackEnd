using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Imports;
using Wolverine;

namespace TaxVision.Customer.Application.Imports.Consumers;

/// <summary>
/// Reacciona al resultado del escaneo asincrono (ClamAV + politica de contenido) del archivo
/// de import subido directo a MinIO (ver CustomerImportCloudStorageClient.UploadAsync, Fase D).
///
/// El FileId de estos eventos ES el CustomerImportAttempt.Id — no hace falta persistir una
/// correlacion aparte (ver comentario en ICustomerImportCloudStorageClient). Estos 3 tipos de
/// evento fluyen por el mismo fanout "taxvision-events" que consumen otros servicios (ej.
/// CloudStorage, Notification) para SUS PROPIOS archivos; un FileId que no matchea ningun
/// attempt de este tenant simplemente no es nuestro y se ignora.
/// </summary>
public static class ImportFileScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        ICustomerImportRepository repository,
        IMessageBus bus,
        ILogger<CustomerImportAttempt> logger,
        CancellationToken ct
    )
    {
        var attempt = await repository.GetByIdAsync(msg.FileId, ct);
        if (attempt is null || attempt.TenantId != msg.TenantId || attempt.Status != ImportStatus.Queued)
            return;

        logger.LogInformation("Import {AttemptId} file passed the scan; queuing worker.", attempt.Id);
        await bus.PublishAsync(new RunCustomerImportMessage(attempt.Id));
    }

    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent msg,
        ICustomerImportRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<CustomerImportAttempt> logger,
        CancellationToken ct
    )
    {
        var attempt = await repository.GetByIdAsync(msg.FileId, ct);
        if (attempt is null || attempt.TenantId != msg.TenantId || attempt.IsTerminal)
            return;

        logger.LogWarning("Import {AttemptId} file failed the security scan; failing the attempt.", attempt.Id);
        attempt.Fail("Uploaded file failed the security scan.");
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        ICustomerImportRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<CustomerImportAttempt> logger,
        CancellationToken ct
    )
    {
        var attempt = await repository.GetByIdAsync(msg.FileId, ct);
        if (attempt is null || attempt.TenantId != msg.TenantId || attempt.IsTerminal)
            return;

        logger.LogWarning("Import {AttemptId} file was blocked by content policy; failing the attempt.", attempt.Id);
        attempt.Fail("Uploaded file was blocked by content policy.");
        await unitOfWork.SaveChangesAsync(ct);
    }
}
