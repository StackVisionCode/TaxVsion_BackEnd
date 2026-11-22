using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence.Configurations;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>
/// Totales del dashboard sobre las columnas de centavos. IgnoreQueryFilters + tenant explícito (scope de Wolverine
/// sin tenant ambiental) y el mismo EXISTS de asignación que <see cref="InvoiceSummaryReader"/>. Las sumas por tramo
/// van en SQL a mano: un solo rango por índice y un CASE plano que reparte cada fila en su tramo, porque LINQ no
/// arma un número variable de sumas (los días del mes).
/// </summary>
public sealed class InvoiceDashboardReader(BillingDbContext dbContext) : IInvoiceDashboardReader
{
    private static readonly string CarriedCredit = nameof(InvoicePaymentSource.CarriedCredit);

    public Task<string?> LatestCurrencyAsync(Guid tenantId, Guid? assignedToUserId, CancellationToken ct = default) =>
        Visible(tenantId, assignedToUserId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => i.Currency)
            .FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<long>> CollectedByRangeAsync(
        InvoiceDashboardScope scope,
        IReadOnlyList<DateTime> boundsUtc,
        CancellationToken ct = default
    ) =>
        SumByRangeAsync(
            $"""
            FROM [billing].[InvoicePayments] AS p
            WHERE p.[TenantId] = @tenantId
              AND p.[ReversedAtUtc] IS NULL
              AND p.[Source] <> N'{CarriedCredit}'
              AND EXISTS (
                  SELECT 1 FROM [billing].[Invoices] AS i
                  WHERE i.[Id] = p.[InvoiceId] AND i.[TenantId] = @tenantId
                    AND i.[DeletedAtUtc] IS NULL AND i.[Currency] = @currency{AssignmentFilter(scope)})
            """,
            "p.[PaidAtUtc]",
            $"p.[{InvoiceCentsColumns.Payment}]",
            scope,
            boundsUtc,
            ct
        );

    public Task<IReadOnlyList<long>> InvoicedByRangeAsync(
        InvoiceDashboardScope scope,
        IReadOnlyList<DateTime> boundsUtc,
        CancellationToken ct = default
    ) =>
        SumByRangeAsync(
            $"""
            FROM [billing].[Invoices] AS i
            WHERE i.[TenantId] = @tenantId
              AND i.[DeletedAtUtc] IS NULL AND i.[Currency] = @currency
              AND i.[Status] NOT IN (N'{nameof(InvoiceStatus.Draft)}', N'{nameof(
                InvoiceStatus.Voided
            )}'){AssignmentFilter(scope)}
            """,
            "i.[IssueDateUtc]",
            $"i.[{InvoiceCentsColumns.Total}]",
            scope,
            boundsUtc,
            ct
        );

    public async Task<InvoiceAgingTotals> AgingAsync(
        InvoiceDashboardScope scope,
        InvoiceAgingCutoffs cutoffs,
        CancellationToken ct = default
    ) =>
        await Open(scope)
            .GroupBy(_ => 1)
            .Select(g => new InvoiceAgingTotals(
                g.Sum(x => x.Due),
                g.Count(),
                g.Count(x => x.Base < cutoffs.PastDueBeforeUtc),
                g.Sum(x => x.Base >= cutoffs.Over30BeforeUtc ? x.Due : 0L),
                g.Count(x => x.Base >= cutoffs.Over30BeforeUtc),
                g.Sum(x => x.Base < cutoffs.Over30BeforeUtc && x.Base >= cutoffs.Over60BeforeUtc ? x.Due : 0L),
                g.Count(x => x.Base < cutoffs.Over30BeforeUtc && x.Base >= cutoffs.Over60BeforeUtc),
                g.Sum(x => x.Base < cutoffs.Over60BeforeUtc && x.Base >= cutoffs.Over90BeforeUtc ? x.Due : 0L),
                g.Count(x => x.Base < cutoffs.Over60BeforeUtc && x.Base >= cutoffs.Over90BeforeUtc),
                g.Sum(x => x.Base < cutoffs.Over90BeforeUtc ? x.Due : 0L),
                g.Count(x => x.Base < cutoffs.Over90BeforeUtc)
            ))
            .FirstOrDefaultAsync(ct)
        ?? InvoiceAgingTotals.Empty;

    public async Task<IReadOnlyList<OpenInvoiceRow>> OldestOpenAsync(
        InvoiceDashboardScope scope,
        int take,
        CancellationToken ct = default
    ) =>
        await Open(scope)
            .OrderBy(x => x.Base)
            .ThenBy(x => x.Id)
            .Take(take)
            .Select(x => new OpenInvoiceRow(x.Id, x.InvoiceNumber, x.CustomerId, x.Due, x.Base))
            .ToListAsync(ct);

    private IQueryable<Invoice> Visible(Guid tenantId, Guid? assignedToUserId)
    {
        var query = dbContext
            .Invoices.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DeletedAtUtc == null);

        if (assignedToUserId is { } assignee)
            query = query.Where(i =>
                dbContext
                    .CustomerAssignmentProjections.IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == i.CustomerId && a.UserId == assignee)
            );
        return query;
    }

    // Pendiente: la regla de OpenInvoiceRules, que comparte el listado. Base = vencimiento; si no lo tiene
    // (valor por defecto, anterior a la emisión), la emisión.
    private IQueryable<OpenInvoice> Open(InvoiceDashboardScope scope) =>
        Visible(scope.TenantId, scope.AssignedToUserId)
            .Where(i => i.Currency == scope.Currency)
            .WhereOpen()
            .Select(i => new OpenInvoice
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                CustomerId = i.CustomerId,
                Due = EF.Property<long>(i, InvoiceCentsColumns.AmountDue),
                Base = i.DueDateUtc >= i.IssueDateUtc ? i.DueDateUtc : i.IssueDateUtc,
            });

    private static string AssignmentFilter(InvoiceDashboardScope scope) =>
        scope.AssignedToUserId is null
            ? string.Empty
            : " AND EXISTS (SELECT 1 FROM [billing].[CustomerAssignmentProjections] AS a"
                + " WHERE a.[TenantId] = @tenantId AND a.[CustomerId] = i.[CustomerId] AND a.[UserId] = @userId)";

    // Una fila por tramo con datos: [bounds[k], bounds[k+1]) → índice k. Los tramos vacíos quedan en 0.
    private async Task<IReadOnlyList<long>> SumByRangeAsync(
        string fromWhere,
        string instant,
        string cents,
        InvoiceDashboardScope scope,
        IReadOnlyList<DateTime> bounds,
        CancellationToken ct
    )
    {
        if (bounds.Count < 2)
            return [];

        var last = bounds.Count - 1;
        var bucket = new StringBuilder("CASE");
        for (var k = 1; k < last; k++)
            bucket.Append($" WHEN {instant} < @b{k} THEN {k - 1}");
        bucket.Append($" ELSE {last - 1} END");

        var sql = $"""
            SELECT r.[Bucket], SUM(r.[Cents]) AS [Cents]
            FROM (
                SELECT {bucket} AS [Bucket], {cents} AS [Cents]
                {fromWhere}
                  AND {instant} >= @b0 AND {instant} < @b{last}
            ) AS r
            GROUP BY r.[Bucket]
            """;

        var rows = await dbContext.Database.SqlQueryRaw<RangeSum>(sql, Parameters(scope, bounds)).ToListAsync(ct);
        var sums = new long[last];
        foreach (var row in rows)
            sums[row.Bucket] = row.Cents;
        return sums;
    }

    private static object[] Parameters(InvoiceDashboardScope scope, IReadOnlyList<DateTime> bounds)
    {
        var parameters = new List<SqlParameter>
        {
            new("@tenantId", SqlDbType.UniqueIdentifier) { Value = scope.TenantId },
            new("@currency", SqlDbType.NVarChar, 3) { Value = scope.Currency },
        };
        if (scope.AssignedToUserId is { } userId)
            parameters.Add(new("@userId", SqlDbType.UniqueIdentifier) { Value = userId });
        for (var k = 0; k < bounds.Count; k++)
            parameters.Add(new($"@b{k}", SqlDbType.DateTime2) { Value = bounds[k] });
        return [.. parameters];
    }

    // Inicializador de miembros, no constructor: EF traduce los accesos a sus propiedades en el GROUP BY.
    private sealed class OpenInvoice
    {
        public Guid Id { get; init; }
        public string? InvoiceNumber { get; init; }
        public Guid CustomerId { get; init; }
        public long Due { get; init; }
        public DateTime Base { get; init; }
    }

    private sealed class RangeSum
    {
        public int Bucket { get; init; }
        public long Cents { get; init; }
    }
}
