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
using TaxVision.Customer.Domain.ContactPoints;
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

        var createdIds = new List<Guid>();
        var updatedIds = new List<Guid>();

        // ---- Cargar attempt y marcar Validating ----
        Guid tenantId;
        Guid createdByUserId;
        DuplicateStrategy strategy;
        ImportSourceKind sourceKind;
        DateTime completedAtUtc;
        int success = 0,
            updated = 0,
            skipped = 0,
            failed = 0,
            total = 0;

        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var attempt = await repo.GetByIdAsync(attemptId, ct);
                if (attempt is null)
                {
                    logger.LogWarning("Import attempt {AttemptId} not found; dropping message", attemptId);
                    return;
                }
                if (attempt.IsTerminal)
                {
                    logger.LogInformation(
                        "Import attempt {AttemptId} already terminal ({Status}); dropping",
                        attemptId,
                        attempt.Status
                    );
                    return;
                }

                tenantId = attempt.TenantId;
                createdByUserId = attempt.CreatedByUserId;
                strategy = attempt.Strategy;
                sourceKind = attempt.SourceKind;

                var startRes = attempt.Start();
                if (startRes.IsFailure)
                {
                    logger.LogWarning("Cannot start import {AttemptId}: {Error}", attemptId, startRes.Error.Message);
                    return;
                }
                await uow.SaveChangesAsync(ct);
            }

            // ---- Leer todas las filas en memoria (max 10MB segun guard del POST) ----
            var allRows = new List<ImportCustomerRow>();
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var sp = scope.ServiceProvider;
                var fileStore = sp.GetRequiredService<IImportFileStore>();
                var readerFactory = sp.GetRequiredService<ICustomerImportReaderFactory>();
                var reader = readerFactory.Resolve(sourceKind);

                await using var fileStream = await fileStore.OpenReadAsync(attemptId, ct);
                await foreach (var row in reader.ReadAsync(fileStream, ct))
                {
                    allRows.Add(row);
                }
            }

            total = allRows.Count;
            var config = scopeFactory.CreateAsyncScope();
            var maxRows =
                config.ServiceProvider.GetRequiredService<IConfiguration>().GetValue<int?>("CustomerImport:MaxRows")
                ?? 10_000;
            await config.DisposeAsync();

            if (total == 0)
            {
                await FailAttemptAsync(scopeFactory, attemptId, "File is empty.", ct);
                return;
            }
            if (total > maxRows)
            {
                await FailAttemptAsync(
                    scopeFactory,
                    attemptId,
                    $"File has {total} rows; maximum allowed is {maxRows}.",
                    ct
                );
                return;
            }

            // ---- Set TotalRows + Move to Applying ----
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var attempt = await repo.GetByIdAsync(attemptId, ct);
                if (attempt is null)
                    return;
                attempt.SetTotalRows(total);
                var moveRes = attempt.MoveToApplying();
                if (moveRes.IsFailure)
                {
                    logger.LogWarning("Cannot move to Applying: {Error}", moveRes.Error.Message);
                    return;
                }
                await uow.SaveChangesAsync(ct);
            }

            // ---- Procesar chunks ----
            var chunks = Chunk(allRows, ChunkSize);
            foreach (var chunk in chunks)
            {
                // Cancelacion cooperativa: chequear antes de cada chunk
                var canceled = await CheckCancelAsync(scopeFactory, attemptId, ct);
                if (canceled)
                {
                    logger.LogInformation("Import {AttemptId} canceled mid-flight; stopping.", attemptId);
                    break;
                }

                var chunkResult = await ProcessChunkAsync(
                    scopeFactory,
                    attemptId,
                    tenantId,
                    createdByUserId,
                    strategy,
                    chunk,
                    logger,
                    ct
                );

                success += chunkResult.SuccessIds.Count;
                updated += chunkResult.UpdatedIds.Count;
                skipped += chunkResult.SkippedCount;
                failed += chunkResult.FailedCount;
                createdIds.AddRange(chunkResult.SuccessIds);
                updatedIds.AddRange(chunkResult.UpdatedIds);
            }

            // ---- Marcar Completed o ConfirmCanceled + publicar evento ----
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerImportRepository>();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                var corr = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
                var fileStore = scope.ServiceProvider.GetRequiredService<IImportFileStore>();

                var attempt = await repo.GetByIdAsync(attemptId, ct);
                if (attempt is null)
                    return;

                if (attempt.Status == ImportStatus.Canceling)
                    attempt.ConfirmCanceled();
                else
                    attempt.Complete();

                completedAtUtc = attempt.CompletedAtUtc ?? DateTime.UtcNow;

                await uow.SaveChangesAsync(ct);

                // Borrar el binario del archivo: el reporte por fila queda en CustomerImportRows
                await fileStore.DeleteAsync(attemptId, ct);

                await bus.PublishAsync(
                    new CustomersBulkImportedIntegrationEvent
                    {
                        TenantId = tenantId,
                        CorrelationId = corr.CorrelationId,
                        ImportJobId = attemptId,
                        CreatedByUserId = createdByUserId,
                        CompletedAtUtc = completedAtUtc,
                        TotalRows = total,
                        SuccessCount = success,
                        UpdatedCount = updated,
                        SkippedCount = skipped,
                        FailedCount = failed,
                        CreatedCustomerIds = createdIds,
                        UpdatedCustomerIds = updatedIds,
                    }
                );

                logger.LogInformation(
                    "Import {AttemptId} finished: total={Total} success={Success} updated={Updated} skipped={Skipped} failed={Failed}",
                    attemptId,
                    total,
                    success,
                    updated,
                    skipped,
                    failed
                );
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import {AttemptId} crashed", attemptId);
            await FailAttemptAsync(scopeFactory, attemptId, ex.Message, CancellationToken.None);
        }
    }

    // ============== Chunk processing ==============

    private sealed record ChunkResult(
        IReadOnlyList<Guid> SuccessIds,
        IReadOnlyList<Guid> UpdatedIds,
        int SkippedCount,
        int FailedCount
    );

    private static async Task<ChunkResult> ProcessChunkAsync(
        IServiceScopeFactory scopeFactory,
        Guid attemptId,
        Guid tenantId,
        Guid createdByUserId,
        DuplicateStrategy strategy,
        IReadOnlyList<ImportCustomerRow> chunk,
        ILogger logger,
        CancellationToken ct
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
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

        // ---- Detectar duplicados batch ----
        var matches = await detector.FindDuplicatesAsync(tenantId, chunk, ct);
        var matchByRow = matches.ToDictionary(m => m.RowNumber);

        // ---- Detectar duplicados intra-chunk en 4 senales (mismas que el detector BD) ----
        // Razon: el detector de BD consulta UNA vez al inicio del chunk. Filas con la misma
        // email/phone/name+DOB dentro del MISMO chunk no son visibles unas a otras hasta el
        // SaveChanges final, asi que sin estos HashSets se crearian customers duplicados.
        var seenBlindIndexes = new HashSet<string>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPhones = new HashSet<string>();
        var seenNameDob = new HashSet<(string Name, DateOnly Dob)>();

        var successIds = new List<Guid>();
        var updatedIds = new List<Guid>();
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var raw in chunk)
        {
            // ---- Parse + validacion ----
            var parsed = ImportRowParser.Parse(raw);
            if (parsed.IsFailure)
            {
                attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), parsed.Error.Code, parsed.Error.Message);
                failedCount++;
                continue;
            }
            var pr = parsed.Value;

            // ---- Resolver catalogos (estricto: si no existe, fila falla) ----
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
                    failedCount++;
                    continue;
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
                    failedCount++;
                    continue;
                }
            }

            // ---- Reconstruir BusinessIdentity con naicsId resuelto ----
            var businessIdentity = pr.BusinessIdentity;
            if (businessIdentity is not null && naicsId is not null)
            {
                var rebuilt = TaxVision.Customer.Domain.Customers.ValueObjects.BusinessIdentity.Create(
                    legalName: businessIdentity.LegalName,
                    structure: businessIdentity.Structure,
                    dba: businessIdentity.Dba,
                    formationDate: businessIdentity.FormationDate,
                    principalBusinessActivityId: naicsId
                );
                if (rebuilt.IsFailure)
                {
                    attempt.RecordFailed(
                        raw.RowNumber,
                        BuildDisplayName(raw),
                        rebuilt.Error.Code,
                        rebuilt.Error.Message
                    );
                    failedCount++;
                    continue;
                }
                businessIdentity = rebuilt.Value;
            }

            // ---- Intra-chunk dedup por las 4 senales (mismas prioridades que el detector BD) ----
            // Prioridad 1: SSN/EIN blind index (mas especifico)
            string? blindIndex = null;
            if (!string.IsNullOrEmpty(pr.NormalizedTaxIdentifier))
            {
                blindIndex = protector.ComputeBlindIndex(pr.NormalizedTaxIdentifier, tenantId);
                if (!seenBlindIndexes.Add(blindIndex))
                {
                    attempt.RecordFailed(
                        raw.RowNumber,
                        BuildDisplayName(raw),
                        "Import.DuplicateInChunk",
                        "Another row in the same chunk has the same tax identifier."
                    );
                    failedCount++;
                    continue;
                }
            }

            // Prioridad 2: Email normalizado
            var normalizedEmail = pr.Email.NormalizedValue;
            if (!seenEmails.Add(normalizedEmail))
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Import.DuplicateInChunk",
                    "Another row in the same chunk has the same email."
                );
                failedCount++;
                continue;
            }

            // Prioridad 3: Phone E.164 (solo si la fila trae phone)
            if (pr.Phone is not null && !seenPhones.Add(pr.Phone.E164Value))
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Import.DuplicateInChunk",
                    "Another row in the same chunk has the same phone."
                );
                failedCount++;
                continue;
            }

            // Prioridad 4: (Nombre + DOB) solo aplica a Individual con DOB
            if (pr.Kind == CustomerKind.Individual && pr.PersonalName is not null && pr.DateOfBirth.HasValue)
            {
                var normalizedName = pr.PersonalName.DisplayName.ToLowerInvariant();
                if (!seenNameDob.Add((normalizedName, pr.DateOfBirth.Value)))
                {
                    attempt.RecordFailed(
                        raw.RowNumber,
                        BuildDisplayName(raw),
                        "Import.DuplicateInChunk",
                        "Another row in the same chunk has the same name + date of birth."
                    );
                    failedCount++;
                    continue;
                }
            }

            // ---- Aplicar estrategia segun match con DB ----
            if (matchByRow.TryGetValue(raw.RowNumber, out var match))
            {
                if (strategy == DuplicateStrategy.Skip)
                {
                    attempt.RecordSkipped(
                        raw.RowNumber,
                        match.ExistingCustomerId,
                        match.ExistingDisplayName,
                        match.MatchedBy
                    );
                    skippedCount++;
                    continue;
                }

                // Merge u Overwrite: cargar el existente y actualizar
                var existing = await customerRepo.GetByIdAsync(match.ExistingCustomerId, ct);
                if (existing is null)
                {
                    attempt.RecordFailed(
                        raw.RowNumber,
                        BuildDisplayName(raw),
                        "Import.MatchedThenMissing",
                        "Matched customer disappeared before update."
                    );
                    failedCount++;
                    continue;
                }

                ApplyUpdate(
                    existing,
                    pr,
                    occupationId,
                    businessIdentity,
                    createdByUserId,
                    strategy,
                    blindIndex,
                    protector,
                    tenantId
                );

                attempt.RecordUpdated(raw.RowNumber, existing.Id, existing.DisplayName, match.MatchedBy);
                updatedIds.Add(existing.Id);
                continue;
            }

            // ---- Sin duplicado: crear nuevo ----
            var registerRes = DomainCustomer.Register(
                tenantId: tenantId,
                kind: pr.Kind,
                personalName: pr.PersonalName,
                businessIdentity: businessIdentity,
                primaryEmail: pr.Email,
                primaryPhone: pr.Phone,
                language: pr.Language,
                preferredChannel: pr.PreferredChannel,
                createdByUserId: createdByUserId,
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
                failedCount++;
                continue;
            }

            var newCustomer = registerRes.Value;

            // ---- Direccion ----
            if (pr.Address is not null)
            {
                var addrRes = newCustomer.AddAddress(AddressKind.Home, pr.Address, isPrimary: true, createdByUserId);
                if (addrRes.IsFailure)
                {
                    attempt.RecordFailed(
                        raw.RowNumber,
                        BuildDisplayName(raw),
                        addrRes.Error.Code,
                        addrRes.Error.Message
                    );
                    failedCount++;
                    continue;
                }
            }

            // ---- Fiscal profile (si trae TaxIdentifier) ----
            if (blindIndex is not null)
            {
                var cipher = protector.Protect(pr.NormalizedTaxIdentifier);
                var last4 = pr.NormalizedTaxIdentifier[^4..];
                var fpRes = newCustomer.SetFiscalProfile(
                    subjectKind: pr.FiscalSubjectKind,
                    taxIdentifierCipher: cipher,
                    taxIdentifierBlindIndex: blindIndex,
                    taxIdentifierLast4: last4,
                    filingStatus: pr.FilingStatus,
                    priorYearAgi: pr.PriorYearAgi,
                    isReturningCustomer: pr.IsReturningCustomer,
                    refundBankAccountCipher: null,
                    refundBankRoutingCipher: null,
                    byUserId: createdByUserId
                );
                if (fpRes.IsFailure)
                {
                    attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), fpRes.Error.Code, fpRes.Error.Message);
                    failedCount++;
                    continue;
                }
            }

            // ---- Spouse como CustomerRelation (Spouse + TaxHouseholdMember) ----
            if (pr.Spouse is not null)
            {
                var relRes = newCustomer.AddRelation(
                    kind: RelationshipKind.Spouse,
                    purposes: RelationPurpose.TaxHouseholdMember,
                    name: pr.Spouse.Name,
                    email: pr.Spouse.Email,
                    phone: pr.Spouse.Phone,
                    dateOfBirth: pr.Spouse.DateOfBirth,
                    address: null,
                    byUserId: createdByUserId
                );

                if (relRes.IsSuccess && !string.IsNullOrEmpty(pr.Spouse.NormalizedTaxIdentifier))
                {
                    var spouseCipher = protector.Protect(pr.Spouse.NormalizedTaxIdentifier);
                    var spouseBlind = protector.ComputeBlindIndex(pr.Spouse.NormalizedTaxIdentifier, tenantId);
                    var spouseLast4 = pr.Spouse.NormalizedTaxIdentifier[^4..];

                    var spouseFp = relRes.Value.SetFiscalProfile(
                        role: TaxRelationshipRole.Spouse,
                        taxIdentifierCipher: spouseCipher,
                        taxIdentifierBlindIndex: spouseBlind,
                        taxIdentifierLast4: spouseLast4,
                        taxYear: DateTime.UtcNow.Year,
                        qualifiesAsDependent: false,
                        livedWithTaxpayer: true,
                        byUserId: createdByUserId
                    );

                    // Si el fiscal del spouse falla no rompemos toda la fila; queda la relacion sin fiscal
                    if (spouseFp.IsFailure)
                        logger.LogWarning(
                            "[ROW {Row}] spouse fiscal profile failed: {Error}",
                            raw.RowNumber,
                            spouseFp.Error.Message
                        );
                }
            }

            // ---- Contact points adicionales (si trae phone) ----
            // Nota: phone primary ya esta en Customer.PrimaryPhone. Si quisieramos un
            // CustomerContactPoint paralelo, lo agregamos aqui. Por ahora omitir.

            await customerRepo.AddAsync(newCustomer, ct);
            attempt.RecordSuccess(raw.RowNumber, newCustomer.Id, newCustomer.DisplayName);
            successIds.Add(newCustomer.Id);
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
            // El chunk completo se pierde por rollback implicito.
            // Los rows del CustomerImportRow tambien se rollback porque viven en este mismo scope.
            // Marcar el job como Failed si esto pasa.
            throw;
        }

        return new ChunkResult(successIds, updatedIds, skippedCount, failedCount);
    }

    // ============== Helpers ==============

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
        var attempt = await repo.GetByIdAsync(attemptId, ct);
        if (attempt is null)
            return;
        attempt.Fail(reason);
        await uow.SaveChangesAsync(ct);
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
        ISensitiveDataProtector protector,
        Guid tenantId
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

        // Fiscal profile: si trae TaxIdentifier nuevo, actualizar
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
