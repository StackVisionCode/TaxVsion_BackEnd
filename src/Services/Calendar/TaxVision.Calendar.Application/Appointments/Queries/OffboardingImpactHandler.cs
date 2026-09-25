using TaxVision.Calendar.Application.Appointments.Abstractions;

namespace TaxVision.Calendar.Application.Appointments.Queries;

// Pre-flight de impacto (punto 3.2): cuántas citas vigentes organiza este empleado (para reasignarlas o
// cancelarlas al retirarlo). NowUtc lo pasa el controller para dejar el handler puro y testeable.
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId, DateTime NowUtc);

public sealed record OffboardingImpactResponse(int FutureAppointments);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        IAppointmentRepository appointments,
        CancellationToken ct
    )
    {
        var future = await appointments.CountFutureByOrganizerAsync(query.TenantId, query.UserId, query.NowUtc, ct);
        return new OffboardingImpactResponse(future);
    }
}
