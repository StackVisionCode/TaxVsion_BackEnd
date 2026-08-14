using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;

namespace TaxVision.Calendar.Application.Types.Queries;

public sealed record ListAppointmentTypesQuery(Guid TenantId, bool OnlyActive);

public static class ListAppointmentTypesHandler
{
    public static async Task<Result<IReadOnlyList<AppointmentTypeResponse>>> Handle(
        ListAppointmentTypesQuery query,
        IAppointmentTypeRepository types,
        CancellationToken ct
    )
    {
        var found = await types.ListAsync(query.TenantId, query.OnlyActive, ct);
        var response = new List<AppointmentTypeResponse>();

        foreach (var type in found)
            response.Add(AppointmentTypeResponse.From(type));

        return Result.Success<IReadOnlyList<AppointmentTypeResponse>>(response);
    }
}
