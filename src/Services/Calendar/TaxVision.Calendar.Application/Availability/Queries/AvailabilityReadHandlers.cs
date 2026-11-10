using System.Globalization;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Availability.Abstractions;
using TaxVision.Calendar.Application.Office.Abstractions;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Application.Availability.Queries;

public sealed record ListAvailabilityRulesQuery(Guid TenantId, Guid UserId);

/// <summary>El horario semanal vigente de una persona. No es privado: es cuándo atiende.</summary>
public static class ListAvailabilityRulesHandler
{
    public static async Task<Result<IReadOnlyList<AvailabilityRuleResponse>>> Handle(
        ListAvailabilityRulesQuery query,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        var rules = await availability.ListRulesAsync(query.TenantId, query.UserId, ct);
        return Result.Success<IReadOnlyList<AvailabilityRuleResponse>>(
            rules.OrderBy(r => r.StartTime).Select(AvailabilityRuleResponse.From).ToList()
        );
    }
}

public sealed record ListBlockedTimesQuery(Guid TenantId, Guid UserId, DateTime FromUtc, DateTime ToUtc);

public static class ListBlockedTimesHandler
{
    public const int MaxRangeDays = 400;

    public static async Task<Result<IReadOnlyList<BlockedTimeResponse>>> Handle(
        ListBlockedTimesQuery query,
        IAvailabilityRepository availability,
        CancellationToken ct
    )
    {
        if (query.ToUtc <= query.FromUtc || (query.ToUtc - query.FromUtc).TotalDays > MaxRangeDays)
            return Result.Failure<IReadOnlyList<BlockedTimeResponse>>(AppointmentErrors.RangeInvalid);

        var blocks = await availability.ListBlocksAsync(query.TenantId, query.UserId, query.FromUtc, query.ToUtc, ct);
        return Result.Success<IReadOnlyList<BlockedTimeResponse>>(
            blocks.OrderBy(b => b.StartUtc).Select(BlockedTimeResponse.From).ToList()
        );
    }
}

public sealed record GetOfficeScheduleQuery(Guid TenantId);

public sealed record OfficeHoursResponse(string Day, string Opens, string Closes);

public sealed record OfficeClosureResponse(string Date, string? EndDate, string Label, bool RepeatsYearly);

/// <summary>Zona null = todavía no llegó; sin horario ni cierres = la oficina no los cargó.</summary>
public sealed record OfficeScheduleResponse(
    string? TimeZoneId,
    IReadOnlyList<OfficeHoursResponse> Hours,
    IReadOnlyList<OfficeClosureResponse> Closures
);

/// <summary>El horario y los cierres de la oficina, para "Copy office hours" y para marcar días cerrados.</summary>
public static class GetOfficeScheduleHandler
{
    public static async Task<Result<OfficeScheduleResponse>> Handle(
        GetOfficeScheduleQuery query,
        IOfficeScheduleRepository offices,
        CancellationToken ct
    )
    {
        var office = await offices.GetAsync(query.TenantId, ct);
        if (office is null)
            return Result.Success(new OfficeScheduleResponse(null, [], []));

        return Result.Success(
            new OfficeScheduleResponse(
                office.TimeZoneId,
                office
                    .Hours.Select(h => new OfficeHoursResponse(h.Day.ToString(), Hhmm(h.Opens), Hhmm(h.Closes)))
                    .ToList(),
                office
                    .Closures.Select(c => new OfficeClosureResponse(
                        Ymd(c.Date),
                        c.EndDate is { } end ? Ymd(end) : null,
                        c.Label,
                        c.RepeatsYearly
                    ))
                    .ToList()
            )
        );
    }

    private static string Hhmm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Ymd(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
