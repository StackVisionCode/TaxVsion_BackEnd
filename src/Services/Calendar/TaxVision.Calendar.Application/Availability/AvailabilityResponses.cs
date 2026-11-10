using TaxVision.Calendar.Domain.Availability;

namespace TaxVision.Calendar.Application.Availability;

/// <summary>Una franja semanal: días (máscara, domingo = 1), horas de pared y su zona.</summary>
public sealed record AvailabilityRuleResponse(
    Guid Id,
    Guid UserId,
    int DaysMask,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string TimeZoneId
)
{
    public static AvailabilityRuleResponse From(AvailabilityRule rule) =>
        new(rule.Id, rule.UserId, (int)rule.Days, rule.StartTime, rule.EndTime, rule.TimeZone.Id);
}

public sealed record BlockedTimeResponse(Guid Id, Guid UserId, DateTime StartUtc, DateTime EndUtc, string? Reason)
{
    public static BlockedTimeResponse From(BlockedTime block) =>
        new(block.Id, block.UserId, block.StartUtc, block.EndUtc, block.Reason);
}

/// <summary>Lo que llega del cliente para una franja.</summary>
public sealed record AvailabilityRuleInput(int DaysMask, TimeOnly StartTime, TimeOnly EndTime, string? TimeZoneId);
