using BuildingBlocks.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Security;

/// <summary>
/// Conteo y re-cifrado del ciphertext fiscal de Customer (B-9/X-9). Recorre por lotes los perfiles fiscales
/// de cliente y de relación cuya versión de clave no es la actual y los re-cifra con la primitiva compartida
/// <see cref="SensitiveDataReEncryptor"/> (descifra con la versión del blob, re-cifra con la actual) recomputando
/// el blind index con la clave actual. Las cifras bancarias del refund (sin blind index) se re-cifran en el
/// mismo pase. Commitea cada lote y limpia el tracker: una interrupción deja lo hecho y el resto se retoma solo
/// —las filas re-cifradas quedan en la versión actual y salen del filtro—. Idempotente y reanudable sin cursor.
/// </summary>
public sealed class CustomerFiscalReEncryptionStore(
    CustomerDbContext db,
    ISensitiveDataProtector protector,
    SensitiveDataReEncryptor reEncryptor,
    IOptions<RotatingSecretProtectionOptions> options
) : ICustomerFiscalCiphertextStore
{
    // Tope de lotes por tabla para no quedar en bucle si una fila sigue en conflicto por escritura concurrente.
    private const int MaxBatchesPerTable = 100_000;

    private short CurrentVersion => options.Value.MasterKeyVersion;

    public async Task<IReadOnlyDictionary<short, long>> CountByKeyVersionAsync(CancellationToken ct = default)
    {
        var totals = new Dictionary<short, long>();

        await AccumulateAsync(
            db.CustomerFiscalProfiles.IgnoreQueryFilters().GroupBy(fp => fp.TaxIdentifierKeyVersion),
            totals,
            ct
        );
        await AccumulateAsync(
            db.CustomerRelationFiscalProfiles.IgnoreQueryFilters().GroupBy(fp => fp.TaxIdentifierKeyVersion),
            totals,
            ct
        );

        return totals;
    }

    public async Task<CustomerFiscalReEncryptionReport> ReEncryptToCurrentAsync(
        int batchSize,
        CancellationToken ct = default
    )
    {
        if (batchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var current = CurrentVersion;
        var tables = new List<CustomerFiscalTableReEncryption>
        {
            new(
                "CustomerFiscalProfiles",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .CustomerFiscalProfiles.IgnoreQueryFilters()
                            .Where(fp => fp.TaxIdentifierKeyVersion != current)
                            .OrderBy(fp => fp.Id)
                            .Take(batchSize),
                    profile =>
                    {
                        var re = reEncryptor.ReEncrypt(profile.TaxIdentifierCipher, profile.TenantId);
                        profile.ReEncryptTaxIdentifier(re.Cipher, re.BlindIndex, current);
                        // Las cifras bancarias (sin blind index) viajan con la fila: se refrescan a la clave actual.
                        if (profile.RefundBankAccountCipher is not null || profile.RefundBankRoutingCipher is not null)
                            profile.ReEncryptRefundBank(
                                ReEncryptBank(profile.RefundBankAccountCipher),
                                ReEncryptBank(profile.RefundBankRoutingCipher)
                            );
                    },
                    ct
                )
            ),
            new(
                "CustomerRelationFiscalProfiles",
                await ReEncryptTableAsync(
                    () =>
                        db
                            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
                            .Where(fp => fp.TaxIdentifierKeyVersion != current)
                            .OrderBy(fp => fp.Id)
                            .Take(batchSize),
                    profile =>
                    {
                        var re = reEncryptor.ReEncrypt(profile.TaxIdentifierCipher, profile.TenantId);
                        profile.ReEncryptTaxIdentifier(re.Cipher, re.BlindIndex, current);
                    },
                    ct
                )
            ),
        };

        return new CustomerFiscalReEncryptionReport(current, tables);
    }

    private byte[]? ReEncryptBank(byte[]? cipher) =>
        cipher is null ? null : protector.Protect(protector.Unprotect(cipher));

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
            // La cancelación se honra entre lotes y al leer; un lote ya leído se commitea entero. Así el apagado
            // nunca corta un SaveChanges a mitad (eso envenena la conexión del pool) y lo hecho queda firme.
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
