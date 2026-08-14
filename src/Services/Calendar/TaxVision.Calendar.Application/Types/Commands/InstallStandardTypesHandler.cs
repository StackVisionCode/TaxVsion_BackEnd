using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Types.Abstractions;
using TaxVision.Calendar.Application.Types.Queries;

namespace TaxVision.Calendar.Application.Types.Commands;

public sealed record InstallStandardTypesCommand(Guid TenantId);

public static class InstallStandardTypesHandler
{
    public static async Task<Result<IReadOnlyList<AppointmentTypeResponse>>> Handle(
        InstallStandardTypesCommand command,
        IAppointmentTypeRepository types,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Idempotente por presencia: reinstalar sobre un catalogo ya tocado duplicaria los tipos.
        if (await types.AnyAsync(command.TenantId, ct))
            return await ListAppointmentTypesHandler.Handle(
                new ListAppointmentTypesQuery(command.TenantId, true),
                types,
                ct
            );

        var built = StandardAppointmentTypes.Build(command.TenantId, DateTime.UtcNow);
        if (built.IsFailure)
            return Result.Failure<IReadOnlyList<AppointmentTypeResponse>>(built.Error);

        var response = new List<AppointmentTypeResponse>();
        foreach (var type in built.Value)
        {
            types.Add(type);
            response.Add(AppointmentTypeResponse.From(type));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success<IReadOnlyList<AppointmentTypeResponse>>(response);
    }
}
