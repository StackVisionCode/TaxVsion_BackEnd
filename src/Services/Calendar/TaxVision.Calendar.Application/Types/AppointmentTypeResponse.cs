using TaxVision.Calendar.Domain.Types;

namespace TaxVision.Calendar.Application.Types;

public sealed record AppointmentTypeResponse(
    Guid Id,
    string Name,
    TimeSpan DefaultDuration,
    string ColorHex,
    bool IsVirtual,
    bool BlocksOnConflict,
    int? DailyCap,
    bool IsActive
)
{
    public static AppointmentTypeResponse From(AppointmentType type) =>
        new(
            type.Id,
            type.Name,
            type.DefaultDuration,
            type.ColorHex,
            type.IsVirtual,
            type.BlocksOnConflict,
            type.DailyCap,
            type.IsActive
        );
}
