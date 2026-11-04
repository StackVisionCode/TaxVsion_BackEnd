using TaxVision.Tenant.Domain.Profiles;

namespace TaxVision.Tenant.Api.Contracts;

/// <summary>Día ISO en inglés ("Monday") y horas <c>HH:mm</c>.</summary>
public sealed record BusinessHoursRequest(string? Day, string? Opens, string? Closes)
{
    public BusinessHoursDraft ToDraft() => new(Day, Opens, Closes);
}
