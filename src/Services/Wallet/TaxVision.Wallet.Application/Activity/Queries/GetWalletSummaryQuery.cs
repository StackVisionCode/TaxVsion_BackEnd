using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Pricing;

namespace TaxVision.Wallet.Application.Activity.Queries;

/// <summary>Resumen de gasto del rango (por defecto, el mes de la oficina hasta hoy) contra el periodo anterior.</summary>
public sealed record GetWalletSummaryQuery(Guid TenantId, string? From, string? To);

public static class GetWalletSummaryHandler
{
    public static async Task<Result<WalletSummaryView>> Handle(
        GetWalletSummaryQuery query,
        IWalletActivityReader reader,
        IWalletRepository wallets,
        IOfficeTimeZone officeTimeZone,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var zone = await officeTimeZone.ResolveAsync(query.TenantId, ct);
        var period = SummaryPeriod.Create(
            query.From,
            query.To,
            zone,
            CivilDate.Of(clock.GetUtcNow().UtcDateTime, zone)
        );
        if (period.IsFailure)
            return Result.Failure<WalletSummaryView>(period.Error);

        var p = period.Value;
        var rows = await reader.ListSpendAsync(
            query.TenantId,
            LocalDay.StartUtc(p.LoadFrom, zone),
            LocalDay.StartUtc(p.LoadTo.AddDays(1), zone),
            ct
        );
        var versions = rows.Where(r => r.PriceBookVersion is not null)
            .Select(r => r.PriceBookVersion!.Value)
            .Distinct()
            .ToList();
        var prices = versions.Count == 0 ? null : await reader.GetUnitPricesAsync(versions, ct);
        var wallet = await wallets.GetByTenantAsync(query.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);

        return Result.Success(
            SpendSummary.Build(
                p,
                rows,
                prices,
                zone,
                wallet?.AvailableMicros ?? 0,
                Domain.Wallet.Wallet.DefaultCurrency
            )
        );
    }
}

/// <summary>Días del rango pedido, del periodo anterior del mismo largo y de los últimos 30 días (promedio).</summary>
internal sealed record SummaryPeriod(DateOnly From, DateOnly To, DateOnly Today)
{
    public const int MaxDays = 366;
    public const int AverageDays = 30;

    public int Days => To.DayNumber - From.DayNumber + 1;
    public DateOnly PreviousFrom => From.AddDays(-Days);
    public DateOnly PreviousTo => From.AddDays(-1);
    public DateOnly AverageFrom => Today.AddDays(-(AverageDays - 1));
    public DateOnly LoadFrom => PreviousFrom < AverageFrom ? PreviousFrom : AverageFrom;
    public DateOnly LoadTo => To > Today ? To : Today;

    public static Result<SummaryPeriod> Create(string? from, string? to, ResolvedTimeZone zone, DateOnly today)
    {
        var fromDay = OfficeDates.ToOfficeDay(from, zone);
        var toDay = OfficeDates.ToOfficeDay(to, zone, exclusiveEnd: true);
        if (fromDay.IsFailure || toDay.IsFailure)
            return Result.Failure<SummaryPeriod>(WalletActivityErrors.InvalidDateRange);

        var period = new SummaryPeriod(
            fromDay.Value ?? new DateOnly(today.Year, today.Month, 1),
            toDay.Value ?? today,
            today
        );
        return period.Days is < 1 or > MaxDays
            ? Result.Failure<SummaryPeriod>(WalletActivityErrors.InvalidDateRange)
            : Result.Success(period);
    }
}

internal static class SpendSummary
{
    private const int MaxDailyBuckets = 31;

    public static WalletSummaryView Build(
        SummaryPeriod p,
        IReadOnlyList<SpendRow> rows,
        IReadOnlyDictionary<int, IReadOnlyDictionary<PriceChannel, long>>? prices,
        ResolvedTimeZone zone,
        long availableMicros,
        string currency
    )
    {
        var dated = rows.Select(r => (Day: CivilDate.Of(r.CreatedAtUtc, zone), Row: r)).ToList();
        var current = dated.Where(d => d.Day >= p.From && d.Day <= p.To).ToList();
        var previous = dated.Where(d => d.Day >= p.PreviousFrom && d.Day <= p.PreviousTo).Sum(d => d.Row.SpentMicros);
        var average =
            dated.Where(d => d.Day >= p.AverageFrom && d.Day <= p.Today).Sum(d => d.Row.SpentMicros)
            / SummaryPeriod.AverageDays;
        var byChannel = current
            .SelectMany(d =>
                ChannelSpend.Split(
                    d.Row.SpentMicros,
                    d.Row.Units,
                    d.Row.PriceBookVersion is { } v ? prices?.GetValueOrDefault(v) : null
                )
            )
            .GroupBy(s => s.Channel)
            .Select(g => new ChannelSpendView(g.Key, g.Sum(s => s.Micros)))
            .OrderByDescending(c => c.SpentMicros)
            .ToList();

        return new WalletSummaryView(
            currency,
            p.From,
            p.To,
            zone.Id,
            current.Sum(d => d.Row.SpentMicros),
            previous,
            Buckets(p, current),
            byChannel,
            average,
            average > 0 ? (int)Math.Min(int.MaxValue, Math.Max(0, availableMicros) / average) : null
        );
    }

    private static List<SpendBucketView> Buckets(SummaryPeriod p, List<(DateOnly Day, SpendRow Row)> current)
    {
        var weekly = p.Days > MaxDailyBuckets;
        DateOnly Key(DateOnly day) => weekly ? day.AddDays(-(((int)day.DayOfWeek + 6) % 7)) : day;

        var sums = current.GroupBy(d => Key(d.Day)).ToDictionary(g => g.Key, g => g.Sum(d => d.Row.SpentMicros));
        var buckets = new List<SpendBucketView>();
        for (var day = Key(p.From); day <= p.To; day = day.AddDays(weekly ? 7 : 1))
            buckets.Add(new SpendBucketView(day, sums.GetValueOrDefault(day)));
        return buckets;
    }
}
