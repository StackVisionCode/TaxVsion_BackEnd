using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Imports;
using Wolverine;

namespace TaxVision.Customer.Application.Imports.Commands.StartCustomerImport;

public static class StartCustomerImportHandler
{
    public static async Task<Result<CustomerImportAttemptResponse>> Handle(
        StartCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        IImportFileStore fileStore,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IConfiguration config,
        ILogger<StartCustomerImportCommand> logger,
        CancellationToken ct
    )
    {
        // ---- Idempotency: si ya existe attempt con la misma key, devolverlo ----
        var existing = await repository.GetByIdempotencyKeyAsync(cmd.TenantId, cmd.IdempotencyKey, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "Idempotent replay: returning existing import attempt {AttemptId} for key {Key}",
                existing.Id,
                cmd.IdempotencyKey
            );
            return Result.Success(Map(existing));
        }

        // ---- Best-effort rate limit: 1 job activo por tenant.
        // El check definitivo lo hace el indice filtrado unique UX_CustomerImportAttempts_Tenant_Active
        // en SaveChanges. Este pre-check es solo para evitar el round-trip a BD en el caso comun. ----
        var activeCount = await repository.CountActiveByTenantAsync(cmd.TenantId, ct);
        if (activeCount >= 1)
            return Result.Failure<CustomerImportAttemptResponse>(
                new Error(
                    "Import.AlreadyRunning",
                    "Another import is already in progress for this tenant. Wait until it finishes or cancel it."
                )
            );

        var attemptResult = MaxByFileCustomerImport(cmd, config);

        if (attemptResult.IsFailure)
            return Result.Failure<CustomerImportAttemptResponse>(attemptResult.Error);

        var attempt = attemptResult.Value;

        await repository.AddAsync(attempt, ct);
        await fileStore.SaveAsync(attempt.Id, cmd.FileBytes, ct);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException ex)
        {
            // SaveChanges golpeo un unique index. Dos posibilidades:
            //   - IX_CustomerImportAttempts_Tenant_IdempotencyKey: otro POST concurrente con la misma key gano
            //   - UX_CustomerImportAttempts_Tenant_Active: otro POST concurrente creo un job activo
            // CustomerDbContext mapea ambos casos a ConflictException.
            logger.LogWarning(
                ex,
                "SaveChanges hit unique violation for tenant {TenantId} key {Key} (concurrent POST)",
                cmd.TenantId,
                cmd.IdempotencyKey
            );

            // Reintentar lookup por idempotency key. Si aparece, fue idempotency-race -> exito.
            var concurrent = await repository.GetByIdempotencyKeyAsync(cmd.TenantId, cmd.IdempotencyKey, ct);
            if (concurrent is not null)
                return Result.Success(Map(concurrent));

            // Si no aparecio, fue el indice de activos -> hay un job activo distinto.
            return Result.Failure<CustomerImportAttemptResponse>(
                new Error(
                    "Import.AlreadyRunning",
                    "Another import is already in progress for this tenant. Wait until it finishes or cancel it."
                )
            );
        }

        // ---- Encolar el worker. Wolverine outbox garantiza que el mensaje se publica
        // si la transaccion HTTP commitea, o se descarta si revierte. ----
        await bus.PublishAsync(new RunCustomerImportMessage(attempt.Id));

        logger.LogInformation(
            "Import attempt {AttemptId} queued for tenant {TenantId} (idempotencyKey={Key}, file={File}, kind={Kind})",
            attempt.Id,
            cmd.TenantId,
            cmd.IdempotencyKey,
            cmd.SourceFileName,
            cmd.SourceKind
        );

        return Result.Success(Map(attempt));
    }

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

    private static Result<CustomerImportAttempt> MaxByFileCustomerImport(
        StartCustomerImportCommand cmd,
        IConfiguration config
    )
    {
        var maxBytes = config.GetValue<int?>("CustomerImport:MaxFileBytes") ?? 10 * 1024 * 1024;
        if (cmd.FileBytes.Length == 0)
            return Result.Failure<CustomerImportAttempt>(new Error("Import.EmptyFile", "Uploaded file is empty."));
        if (cmd.FileBytes.Length > maxBytes)
            return Result.Failure<CustomerImportAttempt>(
                new Error("Import.FileTooLarge", $"File exceeds maximum allowed size of {maxBytes} bytes.")
            );

        // ---- Crear aggregate ----
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
}
