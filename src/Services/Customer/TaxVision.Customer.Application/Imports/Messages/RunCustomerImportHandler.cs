using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Domain.Relations;
using Wolverine;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Worker que procesa el archivo de import en chunks. Corre en background via Wolverine.
///
/// Diseno:
///  - Cada chunk se procesa en su propio scope DI (transaccion-per-chunk). Un chunk malo no
///    aborta el job; rollback solo afecta a su chunk.
///  - El attempt aggregate vive en un scope separado para que su progreso commitee
///    independientemente de los chunks de customers.
///  - Cancelacion cooperativa: antes de cada chunk se recarga el estado del attempt;
///    si esta en Canceling, se confirma cancelacion y se sale.
///  - 1 solo evento CustomersBulkImportedV1 al final (no por fila).
///  - SSN/EIN siempre cifrado AES-GCM + blind index HMAC-tenant para dedup sin texto claro.
/// </summary>
public static class RunCustomerImportHandler
{
    private const int ChunkSize = 500;

    public static async Task Handle(
        RunCustomerImportMessage msg,
        IServiceScopeFactory scopeFactory,
        ILogger<RunCustomerImportMessage> logger,
        CancellationToken ct
    )
    {
        var attemptId = msg.ImportAttemptId;
        logger.LogInformation("Worker received import {AttemptId}", attemptId);

        try
        {
            var startInfo = await StartAttemptAsync(scopeFactory, attemptId, logger, ct);
            if (startInfo is null)
                return;

            var allRows = await ReadRowsAsync(scopeFactory, startInfo.TenantId, attemptId, startInfo.SourceKind, ct);
            var maxRows = ResolveMaxRows(scopeFactory);
            if (!await ValidateRowCountAsync(scopeFactory, attemptId, allRows.Count, maxRows, ct))
                return;

            if (!await MoveToApplyingAsync(scopeFactory, attemptId, allRows.Count, logger, ct))
                return;

            var aggregate = await ProcessAllChunksAsync(scopeFactory, attemptId, startInfo, allRows, logger, ct);

            await FinishAttemptAsync(scopeFactory, attemptId, startInfo, aggregate, allRows.Count, logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Import {AttemptId} interrupted by host shutdown", attemptId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import {AttemptId} crashed", attemptId);
            await FailAttemptAsync(scopeFactory, attemptId, ex.Message, CancellationToken.None);
        }
    }

    // ============== Fase 1: cargar attempt + marcar Validating ==============

    private sealed record StartInfo(
        Guid TenantId,
        Guid CreatedByUserId,
        DuplicateStrategy Strategy,
        ImportSourceKind SourceKind
    );

    private static async Task<StartInfo?> StartAttemptAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
        {
            logger.LogWarning("Import attempt {AttemptId} not found; dropping message", attemptId);
            return null;
        }
        if (attempt.IsTerminal)
        {
            logger.LogInformation(
                "Import attempt {AttemptId} already terminal ({Status}); dropping",
                attemptId,
                attempt.Status
            );
            return null;
        }

        var startRes = attempt.Start();
        if (startRes.IsFailure)
        {
            logger.LogWarning("Cannot start import {AttemptId}: {Error}", attemptId, startRes.Error.Message);
            return null;
        }
        await uow.SaveChangesAsync(ct);

        return new StartInfo(attempt.TenantId, attempt.CreatedByUserId, attempt.Strategy, attempt.SourceKind);
    }

    // ============== Fase 2: leer archivo completo en memoria ==============

    private static async Task<List<ImportCustomerRow>> ReadRowsAsync(
        IServiceScopeFactory sf,
        Guid tenantId,
        Guid attemptId,
        ImportSourceKind sourceKind,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var cloudStorage = sp.GetRequiredService<ICustomerImportCloudStorageClient>();
        var readerFactory = sp.GetRequiredService<ICustomerImportReaderFactory>();
        var reader = readerFactory.Resolve(sourceKind);

        var downloadResult = await cloudStorage.DownloadAsync(tenantId, attemptId, ct);
        if (downloadResult.IsFailure)
            throw new InvalidOperationException(
                $"Could not download the import file from CloudStorage: {downloadResult.Error.Message}"
            );

        var rows = new List<ImportCustomerRow>();
        using var fileStream = new MemoryStream(downloadResult.Value);
        await foreach (var row in reader.ReadAsync(fileStream, ct))
        {
            rows.Add(row);
        }
        return rows;
    }

    private static int ResolveMaxRows(IServiceScopeFactory sf)
    {
        using var scope = sf.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<int?>("CustomerImport:MaxRows")
            ?? 10_000;
    }

    // ============== Fase 3: validar cantidad de filas ==============

    private static async Task<bool> ValidateRowCountAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        int total,
        int maxRows,
        CancellationToken ct
    )
    {
        if (total == 0)
        {
            await FailAttemptAsync(sf, attemptId, "File is empty.", ct);
            return false;
        }
        if (total > maxRows)
        {
            await FailAttemptAsync(sf, attemptId, $"File has {total} rows; maximum allowed is {maxRows}.", ct);
            return false;
        }
        return true;
    }

    // ============== Fase 4: setear TotalRows + mover a Applying ==============

    private static async Task<bool> MoveToApplyingAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        int total,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return false;

        attempt.SetTotalRows(total);
        var moveRes = attempt.MoveToApplying();
        if (moveRes.IsFailure)
        {
            logger.LogWarning("Cannot move to Applying: {Error}", moveRes.Error.Message);
            return false;
        }
        await uow.SaveChangesAsync(ct);
        return true;
    }

    // ============== Fase 5: procesar todos los chunks ==============

    private sealed record ChunkAggregate(
        int Success,
        int Updated,
        int Skipped,
        int Failed,
        List<Guid> CreatedIds,
        List<Guid> UpdatedIds
    );

    private static async Task<ChunkAggregate> ProcessAllChunksAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        StartInfo info,
        IReadOnlyList<ImportCustomerRow> allRows,
        ILogger logger,
        CancellationToken ct
    )
    {
        var createdIds = new List<Guid>();
        var updatedIds = new List<Guid>();
        int success = 0,
            updated = 0,
            skipped = 0,
            failed = 0;

        foreach (var chunk in Chunk(allRows, ChunkSize))
        {
            if (await CheckCancelAsync(sf, attemptId, ct))
            {
                logger.LogInformation("Import {AttemptId} canceled mid-flight; stopping.", attemptId);
                break;
            }

            var result = await ProcessChunkAsync(sf, attemptId, info, chunk, logger, ct);

            success += result.SuccessIds.Count;
            updated += result.UpdatedIds.Count;
            skipped += result.SkippedCount;
            failed += result.FailedCount;
            createdIds.AddRange(result.SuccessIds);
            updatedIds.AddRange(result.UpdatedIds);
        }

        return new ChunkAggregate(success, updated, skipped, failed, createdIds, updatedIds);
    }

    // ============== Fase 6: marcar terminal + borrar archivo + publicar evento ==============

    private static async Task FinishAttemptAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        StartInfo info,
        ChunkAggregate aggregate,
        int total,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ICustomerImportRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var bus = sp.GetRequiredService<IMessageBus>();
        var correlation = sp.GetRequiredService<ICorrelationContext>();
        var cloudStorage = sp.GetRequiredService<ICustomerImportCloudStorageClient>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return;

        if (attempt.Status == ImportStatus.Canceling)
            attempt.ConfirmCanceled();
        else
            attempt.Complete();

        var completedAtUtc = attempt.CompletedAtUtc ?? DateTime.UtcNow;

        await uow.SaveChangesAsync(ct);

        // Borrar el archivo en CloudStorage: el reporte por fila queda en CustomerImportRows.
        // Best-effort: si falla (red, CloudStorage caido), el import ya quedo terminal para el
        // usuario; CustomerImportCleanupHostedService reintenta el borrado remoto mas tarde.
        var deleteResult = await cloudStorage.DeleteAsync(info.TenantId, attemptId, ct);
        if (deleteResult.IsFailure)
            logger.LogWarning(
                "Could not delete CloudStorage file for import {AttemptId}: {Error}",
                attemptId,
                deleteResult.Error.Message
            );

        await bus.PublishAsync(
            new CustomersBulkImportedIntegrationEvent
            {
                TenantId = info.TenantId,
                CorrelationId = correlation.CorrelationId,
                ImportJobId = attemptId,
                CreatedByUserId = info.CreatedByUserId,
                CompletedAtUtc = completedAtUtc,
                TotalRows = total,
                SuccessCount = aggregate.Success,
                UpdatedCount = aggregate.Updated,
                SkippedCount = aggregate.Skipped,
                FailedCount = aggregate.Failed,
                CreatedCustomerIds = aggregate.CreatedIds,
                UpdatedCustomerIds = aggregate.UpdatedIds,
            }
        );

        logger.LogInformation(
            "Import {AttemptId} finished: total={Total} success={Success} updated={Updated} skipped={Skipped} failed={Failed}",
            attemptId,
            total,
            aggregate.Success,
            aggregate.Updated,
            aggregate.Skipped,
            aggregate.Failed
        );
    }

    // ============== Chunk processing ==============

    private sealed record ChunkResult(
        IReadOnlyList<Guid> SuccessIds,
        IReadOnlyList<Guid> UpdatedIds,
        int SkippedCount,
        int FailedCount
    );

    private sealed record DedupTrackers(
        HashSet<string> BlindIndexes,
        HashSet<string> Emails,
        HashSet<string> Phones,
        HashSet<(string Name, DateOnly Dob)> NameDob
    );

    private static async Task<ChunkResult> ProcessChunkAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        StartInfo info,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var repo = sp.GetRequiredService<ICustomerImportRepository>();
        var customerRepo = sp.GetRequiredService<ICustomerRepository>();
        var protector = sp.GetRequiredService<ISensitiveDataProtector>();
        var detector = sp.GetRequiredService<ICustomerDuplicateDetector>();
        var catalogs = sp.GetRequiredService<ICatalogResolver>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return new ChunkResult([], [], 0, 0);

        var matchByRow = await BuildDuplicateMapAsync(detector, info.TenantId, chunk, ct);
        var trackers = CreateDedupTrackers();

        var successIds = new List<Guid>();
        var updatedIds = new List<Guid>();
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var raw in chunk)
        {
            var outcome = await ProcessRowAsync(
                raw,
                attempt,
                info,
                matchByRow,
                trackers,
                customerRepo,
                protector,
                catalogs,
                logger,
                ct
            );
            switch (outcome.Kind)
            {
                case RowOutcomeKind.Success:
                    successIds.Add(outcome.CustomerId!.Value);
                    break;
                case RowOutcomeKind.Updated:
                    updatedIds.Add(outcome.CustomerId!.Value);
                    break;
                case RowOutcomeKind.Skipped:
                    skippedCount++;
                    break;
                case RowOutcomeKind.Failed:
                    failedCount++;
                    break;
            }
        }

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Chunk save failed for import {AttemptId} (rows {From}-{To})",
                attemptId,
                chunk[0].RowNumber,
                chunk[^1].RowNumber
            );
            throw;
        }

        return new ChunkResult(successIds, updatedIds, skippedCount, failedCount);
    }

    // ============== Row processing helpers ==============

    private enum RowOutcomeKind
    {
        Success,
        Updated,
        Skipped,
        Failed,
    }

    private sealed record RowOutcome(RowOutcomeKind Kind, Guid? CustomerId = null);

    private static async Task<Dictionary<int, DuplicateMatch>> BuildDuplicateMapAsync(
        ICustomerDuplicateDetector detector,
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        var matches = await detector.FindDuplicatesAsync(tenantId, chunk, ct);
        return matches.ToDictionary(m => m.RowNumber);
    }

    private static DedupTrackers CreateDedupTrackers() =>
        new(BlindIndexes: [], Emails: new HashSet<string>(StringComparer.OrdinalIgnoreCase), Phones: [], NameDob: []);

    private static async Task<RowOutcome> ProcessRowAsync(
        ImportCustomerRow raw,
        CustomerImportAttempt attempt,
        StartInfo info,
        IReadOnlyDictionary<int, DuplicateMatch> matchByRow,
        DedupTrackers trackers,
        ICustomerRepository customerRepo,
        ISensitiveDataProtector protector,
        ICatalogResolver catalogs,
        ILogger logger,
        CancellationToken ct
    )
    {
        var parsed = ImportRowParser.Parse(raw);
        if (parsed.IsFailure)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), parsed.Error.Code, parsed.Error.Message);
            return new RowOutcome(RowOutcomeKind.Failed);
        }
        var pr = parsed.Value;

        var catalogOutcome = await ResolveCatalogsAsync(raw, pr, attempt, catalogs, ct);
        if (catalogOutcome.Failed)
            return new RowOutcome(RowOutcomeKind.Failed);

        var businessIdentity = pr.BusinessIdentity;
        if (businessIdentity is not null && catalogOutcome.NaicsId is not null)
        {
            var rebuiltRes = RebuildBusinessIdentity(businessIdentity, catalogOutcome.NaicsId);
            if (rebuiltRes.IsFailure)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    rebuiltRes.Error.Code,
                    rebuiltRes.Error.Message
                );
                return new RowOutcome(RowOutcomeKind.Failed);
            }
            businessIdentity = rebuiltRes.Value;
        }

        var blindIndex = ComputeBlindIndexIfPresent(pr, info.TenantId, protector);
        var dedupError = CheckIntraChunkDuplicates(pr, blindIndex, trackers);
        if (dedupError is not null)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), dedupError.Value.Code, dedupError.Value.Message);
            return new RowOutcome(RowOutcomeKind.Failed);
        }

        if (matchByRow.TryGetValue(raw.RowNumber, out var match))
            return await HandleMatchedRowAsync(
                raw,
                pr,
                match,
                attempt,
                info,
                catalogOutcome.OccupationId,
                businessIdentity,
                blindIndex,
                customerRepo,
                protector,
                ct
            );

        return await CreateNewCustomerAsync(
            raw,
            pr,
            attempt,
            info,
            catalogOutcome.OccupationId,
            businessIdentity,
            blindIndex,
            customerRepo,
            protector,
            logger,
            ct
        );
    }

    private sealed record CatalogOutcome(Guid? OccupationId, Guid? NaicsId, bool Failed);

    private static async Task<CatalogOutcome> ResolveCatalogsAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        CustomerImportAttempt attempt,
        ICatalogResolver catalogs,
        CancellationToken ct
    )
    {
        Guid? occupationId = null;
        if (!string.IsNullOrWhiteSpace(pr.OccupationName))
        {
            occupationId = await catalogs.ResolveOccupationIdAsync(pr.OccupationName, ct);
            if (occupationId is null)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Catalog.UnknownOccupation",
                    $"Occupation '{pr.OccupationName}' is not in the catalog."
                );
                return new CatalogOutcome(null, null, true);
            }
        }

        Guid? naicsId = null;
        if (pr.Kind == CustomerKind.Business && !string.IsNullOrWhiteSpace(pr.PrincipalBusinessActivityCode))
        {
            naicsId = await catalogs.ResolvePrincipalBusinessActivityIdAsync(pr.PrincipalBusinessActivityCode, ct);
            if (naicsId is null)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Catalog.UnknownNaics",
                    $"NAICS code '{pr.PrincipalBusinessActivityCode}' is not in the catalog."
                );
                return new CatalogOutcome(occupationId, null, true);
            }
        }

        return new CatalogOutcome(occupationId, naicsId, false);
    }

    private static Result<TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity> RebuildBusinessIdentity(
        TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity original,
        Guid? naicsId
    ) =>
        TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity.Create(
            legalName: original.LegalName,
            structure: original.Structure,
            dba: original.Dba,
            formationDate: original.FormationDate,
            principalBusinessActivityId: naicsId
        );

    private static string? ComputeBlindIndexIfPresent(ParsedRow pr, Guid tenantId, ISensitiveDataProtector protector) =>
        string.IsNullOrEmpty(pr.NormalizedTaxIdentifier)
            ? null
            : protector.ComputeBlindIndex(pr.NormalizedTaxIdentifier, tenantId);

    private static (string Code, string Message)? CheckIntraChunkDuplicates(
        ParsedRow pr,
        string? blindIndex,
        DedupTrackers trackers
    )
    {
        if (blindIndex is not null && !trackers.BlindIndexes.Add(blindIndex))
            return ("Import.DuplicateInChunk", "Another row in the same chunk has the same tax identifier.");

        if (!trackers.Emails.Add(pr.Email.NormalizedValue))
            return ("Import.DuplicateInChunk", "Another row in the same chunk has the same email.");

        if (pr.Phone is not null && !trackers.Phones.Add(pr.Phone.E164Value))
            return ("Import.DuplicateInChunk", "Another row in the same chunk has the same phone.");

        if (pr.Kind == CustomerKind.Individual && pr.PersonalName is not null && pr.DateOfBirth.HasValue)
        {
            var normalizedName = pr.PersonalName.DisplayName.ToLowerInvariant();
            if (!trackers.NameDob.Add((normalizedName, pr.DateOfBirth.Value)))
                return ("Import.DuplicateInChunk", "Another row in the same chunk has the same name + date of birth.");
        }

        return null;
    }

    private static async Task<RowOutcome> HandleMatchedRowAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        DuplicateMatch match,
        CustomerImportAttempt attempt,
        StartInfo info,
        Guid? occupationId,
        TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity? businessIdentity,
        string? blindIndex,
        ICustomerRepository customerRepo,
        ISensitiveDataProtector protector,
        CancellationToken ct
    )
    {
        if (info.Strategy == DuplicateStrategy.Skip)
        {
            attempt.RecordSkipped(raw.RowNumber, match.ExistingCustomerId, match.ExistingDisplayName, match.MatchedBy);
            return new RowOutcome(RowOutcomeKind.Skipped);
        }

        var existing = await customerRepo.GetByIdAsync(match.ExistingCustomerId, ct);
        if (existing is null)
        {
            attempt.RecordFailed(
                raw.RowNumber,
                BuildDisplayName(raw),
                "Import.MatchedThenMissing",
                "Matched customer disappeared before update."
            );
            return new RowOutcome(RowOutcomeKind.Failed);
        }

        ApplyUpdate(
            existing,
            pr,
            occupationId,
            businessIdentity,
            info.CreatedByUserId,
            info.Strategy,
            blindIndex,
            protector
        );
        attempt.RecordUpdated(raw.RowNumber, existing.Id, existing.DisplayName, match.MatchedBy);
        return new RowOutcome(RowOutcomeKind.Updated, existing.Id);
    }

    private static async Task<RowOutcome> CreateNewCustomerAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        CustomerImportAttempt attempt,
        StartInfo info,
        Guid? occupationId,
        TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity? businessIdentity,
        string? blindIndex,
        ICustomerRepository customerRepo,
        ISensitiveDataProtector protector,
        ILogger logger,
        CancellationToken ct
    )
    {
        var registerRes = DomainCustomer.Register(
            tenantId: info.TenantId,
            kind: pr.Kind,
            personalName: pr.PersonalName,
            businessIdentity: businessIdentity,
            primaryEmail: pr.Email,
            primaryPhone: pr.Phone,
            language: pr.Language,
            preferredChannel: pr.PreferredChannel,
            createdByUserId: info.CreatedByUserId,
            dateOfBirth: pr.DateOfBirth,
            occupationId: occupationId
        );

        if (registerRes.IsFailure)
        {
            attempt.RecordFailed(
                raw.RowNumber,
                BuildDisplayName(raw),
                registerRes.Error.Code,
                registerRes.Error.Message
            );
            return new RowOutcome(RowOutcomeKind.Failed);
        }

        var newCustomer = registerRes.Value;

        if (pr.Address is not null)
        {
            var addrRes = newCustomer.AddAddress(AddressKind.Home, pr.Address, isPrimary: true, info.CreatedByUserId);
            if (addrRes.IsFailure)
            {
                attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), addrRes.Error.Code, addrRes.Error.Message);
                return new RowOutcome(RowOutcomeKind.Failed);
            }
        }

        if (blindIndex is not null)
        {
            var fpRes = SetPrimaryFiscalProfile(newCustomer, pr, blindIndex, protector, info.CreatedByUserId);
            if (fpRes.IsFailure)
            {
                attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), fpRes.Error.Code, fpRes.Error.Message);
                return new RowOutcome(RowOutcomeKind.Failed);
            }
        }

        if (pr.Spouse is not null)
            AttachSpouseRelation(
                newCustomer,
                pr.Spouse,
                info.TenantId,
                info.CreatedByUserId,
                protector,
                raw.RowNumber,
                logger
            );

        await customerRepo.AddAsync(newCustomer, ct);
        attempt.RecordSuccess(raw.RowNumber, newCustomer.Id, newCustomer.DisplayName);
        return new RowOutcome(RowOutcomeKind.Success, newCustomer.Id);
    }

    private static Result SetPrimaryFiscalProfile(
        DomainCustomer customer,
        ParsedRow pr,
        string blindIndex,
        ISensitiveDataProtector protector,
        Guid byUserId
    )
    {
        var cipher = protector.Protect(pr.NormalizedTaxIdentifier);
        var last4 = pr.NormalizedTaxIdentifier[^4..];
        return customer.SetFiscalProfile(
            subjectKind: pr.FiscalSubjectKind,
            taxIdentifierCipher: cipher,
            taxIdentifierBlindIndex: blindIndex,
            taxIdentifierLast4: last4,
            filingStatus: pr.FilingStatus,
            priorYearAgi: pr.PriorYearAgi,
            isReturningCustomer: pr.IsReturningCustomer,
            refundBankAccountCipher: null,
            refundBankRoutingCipher: null,
            byUserId: byUserId
        );
    }

    private static void AttachSpouseRelation(
        DomainCustomer customer,
        ParsedSpouse spouse,
        Guid tenantId,
        Guid byUserId,
        ISensitiveDataProtector protector,
        int rowNumber,
        ILogger logger
    )
    {
        var relRes = customer.AddRelation(
            kind: RelationshipKind.Spouse,
            purposes: RelationPurpose.TaxHouseholdMember,
            name: spouse.Name,
            email: spouse.Email,
            phone: spouse.Phone,
            dateOfBirth: spouse.DateOfBirth,
            address: null,
            byUserId: byUserId
        );

        if (relRes.IsFailure || string.IsNullOrEmpty(spouse.NormalizedTaxIdentifier))
            return;

        var spouseCipher = protector.Protect(spouse.NormalizedTaxIdentifier);
        var spouseBlind = protector.ComputeBlindIndex(spouse.NormalizedTaxIdentifier, tenantId);
        var spouseLast4 = spouse.NormalizedTaxIdentifier[^4..];

        var spouseFp = relRes.Value.SetFiscalProfile(
            role: TaxRelationshipRole.Spouse,
            taxIdentifierCipher: spouseCipher,
            taxIdentifierBlindIndex: spouseBlind,
            taxIdentifierLast4: spouseLast4,
            taxYear: DateTime.UtcNow.Year,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            byUserId: byUserId
        );

        if (spouseFp.IsFailure)
            logger.LogWarning("[ROW {Row}] spouse fiscal profile failed: {Error}", rowNumber, spouseFp.Error.Message);
    }

    // ============== Helpers compartidos ==============

    private static async Task<bool> CheckCancelAsync(IServiceScopeFactory sf, Guid attemptId, CancellationToken ct)
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var attempt = await repo.GetByIdAsync(attemptId, ct);
        return attempt is null || attempt.IsCancelRequested;
    }

    private static async Task FailAttemptAsync(
        IServiceScopeFactory sf,
        Guid attemptId,
        string reason,
        CancellationToken ct
    )
    {
        await using var scope = sf.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return;
        attempt.Fail(reason);
        var failedAtUtc = attempt.CompletedAtUtc ?? DateTime.UtcNow;
        await uow.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CustomerImportFailedIntegrationEvent
            {
                TenantId = attempt.TenantId,
                CorrelationId = correlation.CorrelationId,
                ImportJobId = attempt.Id,
                CreatedByUserId = attempt.CreatedByUserId,
                Reason = attempt.FailureReason ?? reason,
                FailedAtUtc = failedAtUtc,
            }
        );
    }

    private static IEnumerable<IReadOnlyList<ImportCustomerRow>> Chunk(
        IReadOnlyList<ImportCustomerRow> source,
        int size
    )
    {
        for (int i = 0; i < source.Count; i += size)
        {
            var len = Math.Min(size, source.Count - i);
            var arr = new ImportCustomerRow[len];
            for (int j = 0; j < len; j++)
                arr[j] = source[i + j];
            yield return arr;
        }
    }

    private static string BuildDisplayName(ImportCustomerRow raw)
    {
        if (!string.IsNullOrWhiteSpace(raw.LegalName))
            return raw.LegalName.Trim();
        var name = $"{raw.FirstName} {raw.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "(no name)" : name;
    }

    private static void ApplyUpdate(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity? businessIdentity,
        Guid byUserId,
        DuplicateStrategy strategy,
        string? blindIndex,
        ISensitiveDataProtector protector
    )
    {
        // Merge: solo cambia campos no nulos del archivo SI el existente no los tiene
        // Overwrite: pisa todo
        if (strategy == DuplicateStrategy.Overwrite)
        {
            existing.ChangePreferences(pr.Language, pr.PreferredChannel, byUserId);
            existing.ChangeOccupation(occupationId, byUserId);
            existing.ChangePrimaryEmail(pr.Email, byUserId);
            if (pr.Phone is not null)
                existing.ChangePrimaryPhone(pr.Phone, byUserId);
        }
        else // Merge
        {
            if (existing.OccupationId is null && occupationId is not null)
                existing.ChangeOccupation(occupationId, byUserId);
            if (existing.PrimaryPhone is null && pr.Phone is not null)
                existing.ChangePrimaryPhone(pr.Phone, byUserId);
            // Email y preferencias no se pisan en Merge (son fuente de verdad del existente)
        }

        if (blindIndex is not null)
        {
            var cipher = protector.Protect(pr.NormalizedTaxIdentifier);
            var last4 = pr.NormalizedTaxIdentifier[^4..];
            existing.SetFiscalProfile(
                subjectKind: pr.FiscalSubjectKind,
                taxIdentifierCipher: cipher,
                taxIdentifierBlindIndex: blindIndex,
                taxIdentifierLast4: last4,
                filingStatus: pr.FilingStatus,
                priorYearAgi: pr.PriorYearAgi,
                isReturningCustomer: pr.IsReturningCustomer,
                refundBankAccountCipher: null,
                refundBankRoutingCipher: null,
                byUserId: byUserId
            );
        }
    }
}
