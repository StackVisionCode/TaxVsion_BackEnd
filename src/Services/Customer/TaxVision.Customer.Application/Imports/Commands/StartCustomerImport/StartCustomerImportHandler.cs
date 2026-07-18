using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Configuration;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Commands.StartCustomerImport;

public static class StartCustomerImportHandler
{
    public static async Task<Result<CustomerImportAttemptResponse>> Handle(
        StartCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        ICustomerImportCloudStorageClient cloudStorage,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<CustomerImportOptions> importOptions,
        ILogger<StartCustomerImportCommand> logger,
        CancellationToken ct
    )
    {
        var replay = await CheckIdempotencyReplayAsync(cmd, repository, logger, ct);
        if (replay is not null)
            return Result.Success(replay);

        var rateLimitCheck = await EnforceRateLimitAsync(cmd, repository, ct);
        if (rateLimitCheck.IsFailure)
            return Result.Failure<CustomerImportAttemptResponse>(rateLimitCheck.Error);

        var attemptResult = ValidateFileAndCreateAttempt(cmd, importOptions.CurrentValue);
        if (attemptResult.IsFailure)
            return Result.Failure<CustomerImportAttemptResponse>(attemptResult.Error);

        var persistResult = await PersistAttemptWithRaceHandlingAsync(
            attemptResult.Value,
            cmd,
            repository,
            unitOfWork,
            logger,
            ct
        );
        if (persistResult.IsFailure)
            return Result.Failure<CustomerImportAttemptResponse>(persistResult.Error);

        var finalAttempt = persistResult.Value;

        // Si fue un race idempotente resuelto, ya existe un upload en curso por el ganador; no repetirlo.
        if (finalAttempt.Id == attemptResult.Value.Id)
        {
            // Sube directo a MinIO y publica SaveFileRequestedIntegrationEvent (Fase D) — el
            // worker de import NO se encola aca: ImportFileScanResultConsumer lo hace cuando
            // CloudStorage confirma que el archivo paso el escaneo (FileAvailableIntegrationEvent).
            var uploadResult = await UploadFileAsync(cloudStorage, cmd, finalAttempt.Id, ct);
            if (uploadResult.IsFailure)
            {
                finalAttempt.Fail(uploadResult.Error.Message);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Failure<CustomerImportAttemptResponse>(uploadResult.Error);
            }
            LogQueued(logger, cmd, finalAttempt.Id);
        }

        return Result.Success(Map(finalAttempt));
    }

    // ============== Fase 1: idempotency replay ==============

    private static async Task<CustomerImportAttemptResponse?> CheckIdempotencyReplayAsync(
        StartCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        ILogger logger,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByIdempotencyKeyAsync(cmd.TenantId, cmd.IdempotencyKey, ct);
        if (existing is null)
            return null;

        logger.LogInformation(
            "Idempotent replay: returning existing import attempt {AttemptId} for key {Key}",
            existing.Id,
            cmd.IdempotencyKey
        );
        return Map(existing);
    }

    // ============== Fase 2: rate limit (best-effort pre-check) ==============

    private static async Task<Result> EnforceRateLimitAsync(
        StartCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        CancellationToken ct
    )
    {
        // El check definitivo lo hace el indice filtrado unique UX_CustomerImportAttempts_Tenant_Active
        // en SaveChanges. Este pre-check es solo para evitar el round-trip a BD en el caso comun.
        var activeCount = await repository.CountActiveByTenantAsync(cmd.TenantId, ct);
        if (activeCount >= 1)
            return Result.Failure(
                new Error(
                    "Import.AlreadyRunning",
                    "Another import is already in progress for this tenant. Wait until it finishes or cancel it."
                )
            );
        return Result.Success();
    }

    // ============== Fase 3: validar archivo + crear aggregate ==============

    private static Result<CustomerImportAttempt> ValidateFileAndCreateAttempt(
        StartCustomerImportCommand cmd,
        CustomerImportOptions options
    )
    {
        var maxBytes = options.MaxFileBytes;
        if (cmd.FileBytes.Length == 0)
            return Result.Failure<CustomerImportAttempt>(new Error("Import.EmptyFile", "Uploaded file is empty."));
        if (cmd.FileBytes.Length > maxBytes)
            return Result.Failure<CustomerImportAttempt>(
                new Error("Import.FileTooLarge", $"File exceeds maximum allowed size of {maxBytes} bytes.")
            );

        var attemptResult = CustomerImportAttempt.Create(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            idempotencyKey: cmd.IdempotencyKey,
            strategy: cmd.Strategy,
            sourceKind: cmd.SourceKind,
            sourceFileName: cmd.SourceFileName
        );

        if (attemptResult.IsFailure)
            return Result.Failure<CustomerImportAttempt>(attemptResult.Error);

        return attemptResult;
    }

    // ============== Fase 4: persistir con manejo de race conditions ==============
    // Devuelve el attempt que efectivamente quedo persistido:
    //   - Si el SaveChanges commiteo: el attempt recien creado.
    //   - Si hubo race con misma idempotency key: el attempt del ganador (concurrent).
    //   - Si hubo race con el indice de "activos": Failure con Import.AlreadyRunning.

    private static async Task<Result<CustomerImportAttempt>> PersistAttemptWithRaceHandlingAsync(
        CustomerImportAttempt attempt,
        StartCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct
    )
    {
        await repository.AddAsync(attempt, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(attempt);
        }
        catch (ConflictException ex)
        {
            logger.LogWarning(
                ex,
                "SaveChanges hit unique violation for tenant {TenantId} key {Key} (concurrent POST)",
                cmd.TenantId,
                cmd.IdempotencyKey
            );

            // Reintentar lookup por idempotency key. Si aparece, fue race de misma key -> devolver el ganador.
            var concurrent = await repository.GetByIdempotencyKeyAsync(cmd.TenantId, cmd.IdempotencyKey, ct);
            if (concurrent is not null)
                return Result.Success(concurrent);

            // Si no aparecio, fue el indice de activos -> hay un job activo distinto.
            return Result.Failure<CustomerImportAttempt>(
                new Error(
                    "Import.AlreadyRunning",
                    "Another import is already in progress for this tenant. Wait until it finishes or cancel it."
                )
            );
        }
    }

    // ============== Fase 5: subir el archivo a CloudStorage ==============

    private static Task<Result> UploadFileAsync(
        ICustomerImportCloudStorageClient cloudStorage,
        StartCustomerImportCommand cmd,
        Guid attemptId,
        CancellationToken ct
    ) =>
        cloudStorage.UploadAsync(
            cmd.TenantId,
            attemptId,
            cmd.FileBytes,
            cmd.SourceFileName,
            ContentTypeFor(cmd.SourceKind),
            cmd.CreatedByUserId,
            ct
        );

    private static string ContentTypeFor(ImportSourceKind kind) =>
        kind switch
        {
            ImportSourceKind.Csv => "text/csv",
            ImportSourceKind.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };

    private static void LogQueued(ILogger logger, StartCustomerImportCommand cmd, Guid attemptId) =>
        logger.LogInformation(
            "Import attempt {AttemptId} queued for tenant {TenantId} (idempotencyKey={Key}, file={File}, kind={Kind})",
            attemptId,
            cmd.TenantId,
            cmd.IdempotencyKey,
            cmd.SourceFileName,
            cmd.SourceKind
        );

    // ============== Mapeo aggregate -> response ==============

    private static CustomerImportAttemptResponse Map(CustomerImportAttempt a) =>
        new(
            a.Id,
            a.TenantId,
            a.CreatedByUserId,
            a.IdempotencyKey,
            a.Status,
            a.Strategy,
            a.SourceKind,
            a.SourceFileName,
            a.TotalRows,
            a.ProcessedRows,
            a.SuccessCount,
            a.UpdatedCount,
            a.SkippedCount,
            a.FailedCount,
            a.CreatedAtUtc,
            a.StartedAtUtc,
            a.CompletedAtUtc,
            a.CanceledAtUtc,
            a.CanceledByUserId,
            a.FailureReason
        );
}
