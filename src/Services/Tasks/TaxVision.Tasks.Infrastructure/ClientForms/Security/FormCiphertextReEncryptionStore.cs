using BuildingBlocks.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Security;

/// <summary>
/// Conteo y re-cifrado del ciphertext de Client Forms (X-9.2). Recorre las tablas cifradas por lotes y
/// re-cifra las filas con versión distinta de la actual, usando los mismos protectores que el flujo normal
/// (eligen la clave por la versión de la fila). Commitea cada lote y limpia el tracker: una interrupción
/// deja lo hecho y el resto se retoma solo —las filas re-cifradas ya no caen en el filtro.
/// </summary>
public sealed class FormCiphertextReEncryptionStore(
    TasksDbContext db,
    IAnswersProtector answers,
    ISensitiveValueProtector sensitive,
    IFormExternalAccessSecurity external,
    IOptions<RotatingSecretProtectionOptions> options
) : IClientFormsCiphertextStore
{
    // Tope de lotes por tabla para no quedar en bucle si una fila sigue en conflicto por escritura concurrente.
    private const int MaxBatchesPerTable = 100_000;

    private short CurrentVersion => options.Value.MasterKeyVersion;

    public async Task<IReadOnlyDictionary<short, long>> CountByKeyVersionAsync(CancellationToken ct = default)
    {
        var totals = new Dictionary<short, long>();

        await AccumulateAsync(
            db.FormSubmissions.IgnoreQueryFilters()
                .Where(submission => submission.DraftAnswers != null)
                .GroupBy(submission => submission.DraftAnswers!.KeyVersion),
            totals,
            ct
        );
        await AccumulateAsync(
            db.FormSubmissionRevisions.IgnoreQueryFilters().GroupBy(revision => revision.Answers.KeyVersion),
            totals,
            ct
        );
        await AccumulateAsync(
            db.FormSensitiveAnswers.IgnoreQueryFilters().GroupBy(answer => answer.Protected.KeyVersion),
            totals,
            ct
        );
        await AccumulateAsync(
            db.FormAccessChallenges.IgnoreQueryFilters().GroupBy(challenge => challenge.EmailKeyVersion),
            totals,
            ct
        );
        await AccumulateAsync(
            db.FormIntakeLinks.IgnoreQueryFilters().GroupBy(link => link.TokenKeyVersion),
            totals,
            ct
        );

        return totals;
    }

    public async Task<ClientFormsReEncryptionReport> ReEncryptToCurrentAsync(
        int batchSize,
        CancellationToken ct = default
    )
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var current = CurrentVersion;
        var tables = new List<ClientFormsTableReEncryption>
        {
            new(
                "DraftAnswers",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .FormSubmissions.IgnoreQueryFilters()
                            .Where(s => s.DraftAnswers != null && s.DraftAnswers.KeyVersion != current)
                            .OrderBy(s => s.Id)
                            .Take(batchSize),
                    submission => submission.ReEncryptDraft(ReEncryptAnswers(submission.DraftAnswers!)),
                    ct
                )
            ),
            new(
                "RevisionAnswers",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .FormSubmissionRevisions.IgnoreQueryFilters()
                            .Where(r => r.Answers.KeyVersion != current)
                            .OrderBy(r => r.Id)
                            .Take(batchSize),
                    revision => revision.ReEncryptAnswers(ReEncryptAnswers(revision.Answers)),
                    ct
                )
            ),
            new(
                "SensitiveAnswers",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .FormSensitiveAnswers.IgnoreQueryFilters()
                            .Where(a => a.Protected.KeyVersion != current)
                            .OrderBy(a => a.Id)
                            .Take(batchSize),
                    answer => answer.ReEncrypt(ReEncryptSensitive(answer.Protected)),
                    ct
                )
            ),
            new(
                "ChallengeEmails",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .FormAccessChallenges.IgnoreQueryFilters()
                            .Where(c => c.EmailKeyVersion != current)
                            .OrderBy(c => c.Id)
                            .Take(batchSize),
                    challenge =>
                    {
                        var re = external.ReEncryptEmail(
                            challenge.TenantId,
                            challenge.EmailCipher,
                            challenge.EmailKeyVersion
                        );
                        challenge.ReEncryptEmail(re.Cipher, re.KeyVersion, re.LookupHash);
                    },
                    ct
                )
            ),
            new(
                "IntakeTokens",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .FormIntakeLinks.IgnoreQueryFilters()
                            .Where(l => l.TokenKeyVersion != current)
                            .OrderBy(l => l.Id)
                            .Take(batchSize),
                    link =>
                    {
                        var re = external.ReEncryptToken(link.TokenCipher, link.TokenKeyVersion);
                        link.ReEncryptToken(re.Cipher, re.KeyVersion);
                    },
                    ct
                )
            ),
        };

        return new ClientFormsReEncryptionReport(current, tables);
    }

    private ProtectedAnswers ReEncryptAnswers(ProtectedAnswers value) => answers.Protect(answers.Unprotect(value));

    private ProtectedTaxIdentifier ReEncryptSensitive(ProtectedTaxIdentifier value)
    {
        var plain = sensitive.Unprotect(new ProtectedSensitiveValue(value.Cipher, value.KeyVersion));
        var fresh = sensitive.Protect(plain);
        var rebuilt = ProtectedTaxIdentifier.Create(fresh.Cipher, fresh.KeyVersion, value.Last4);
        if (rebuilt.IsFailure)
            throw new InvalidOperationException("Re-encrypted tax identifier failed validation.");
        return rebuilt.Value;
    }

    private async Task<long> ReEncryptTableAsync<TEntity>(
        Func<IQueryable<TEntity>> batchQuery,
        Action<TEntity> reEncrypt,
        CancellationToken ct
    )
        where TEntity : class
    {
        long total = 0;
        for (var iteration = 0; iteration < MaxBatchesPerTable; iteration++)
        {
            // La cancelación se honra solo entre lotes: cancelar a mitad de una consulta hace que SqlClient
            // aborte el comando con "A severe error occurred" (SqlException, no OperationCanceledException)
            // y puede dejar la conexión del pool inutilizable. Un lote ya leído se termina entero.
            ct.ThrowIfCancellationRequested();
            var batch = await batchQuery().ToListAsync(CancellationToken.None);
            if (batch.Count == 0)
                return total;

            foreach (var entity in batch)
                reEncrypt(entity);

            await db.SaveChangesAsync(CancellationToken.None);
            total += batch.Count;
            // El tracker no debe crecer entre lotes: ya está persistido.
            db.ChangeTracker.Clear();
        }
        return total;
    }

    private static async Task AccumulateAsync<TEntity>(
        IQueryable<IGrouping<short, TEntity>> grouped,
        Dictionary<short, long> totals,
        CancellationToken ct
    )
    {
        var counts = await grouped
            .Select(group => new { Version = group.Key, Count = group.LongCount() })
            .ToListAsync(ct);
        foreach (var row in counts)
            totals[row.Version] = totals.GetValueOrDefault(row.Version) + row.Count;
    }
}
