using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Application.Offboarding;

/// <summary>Avance del retiro de un usuario del tenant (para "completado en N/N pasos") y su ficha.</summary>
public sealed record GetOffboardingStatusQuery(Guid TenantId, Guid UserId);

/// <summary><c>module</c>: nombre visible del área (el que ve la oficina), no el del servicio.</summary>
public sealed record OffboardingPendingStepResponse(string Service, string Step, string Module);

/// <summary>Persona de la ficha (quien hizo el retiro o el sucesor) con su nombre para mostrar.</summary>
public sealed record OffboardingPersonResponse(Guid Id, string Name);

/// <summary>
/// <c>status</c>: InProgress | Completed | Incomplete. La ficha (<c>offboardedBy</c>, <c>successor</c>,
/// <c>reason</c>, <c>reasonNote</c>) es null en retiros anteriores a ella; <c>successor</c> null = sin sucesor.
/// </summary>
public sealed record OffboardingStatusResponse(
    string Status,
    int Expected,
    int Completed,
    IReadOnlyList<OffboardingPendingStepResponse> Pending,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    OffboardingPersonResponse? OffboardedBy = null,
    OffboardingPersonResponse? Successor = null,
    string? Reason = null,
    string? ReasonNote = null
)
{
    public static OffboardingStatusResponse From(
        OffboardingRun run,
        OffboardingPersonResponse? offboardedBy = null,
        OffboardingPersonResponse? successor = null
    ) =>
        new(
            run.Status.ToString(),
            run.ExpectedCount,
            run.CompletedCount,
            run.PendingSteps.Select(s => new OffboardingPendingStepResponse(
                    s.Service,
                    s.Step,
                    OffboardingParticipants.ModuleOf(s.Service)
                ))
                .ToArray(),
            run.StartedAtUtc,
            run.CompletedAtUtc,
            offboardedBy,
            successor,
            run.Reason?.ToString(),
            run.ReasonNote
        );
}

public static class GetOffboardingStatusHandler
{
    public static async Task<Result<OffboardingStatusResponse>> Handle(
        GetOffboardingStatusQuery query,
        IOffboardingRunRepository runs,
        IUserRepository users,
        CancellationToken ct
    )
    {
        var run = await runs.GetByUserAsync(query.TenantId, query.UserId, ct);
        if (run is null)
            return Result.Failure<OffboardingStatusResponse>(
                new Error("User.NotFound", "There is no offboarding record for this user.")
            );

        var offboardedBy = await PersonAsync(run.OffboardedByUserId, query.TenantId, users, ct);
        var successor = await PersonAsync(run.SuccessorUserId, query.TenantId, users, ct);
        return Result.Success(OffboardingStatusResponse.From(run, offboardedBy, successor));
    }

    // Los usuarios no se borran (un retirado sigue existiendo): el nombre se resuelve si es del mismo tenant.
    // De otro tenant (soporte de la plataforma) sale un nombre genérico, sin revelar la cuenta.
    private static async Task<OffboardingPersonResponse?> PersonAsync(
        Guid? userId,
        Guid tenantId,
        IUserRepository users,
        CancellationToken ct
    )
    {
        if (userId is not { } id)
            return null;
        var user = await users.GetByIdAsync(id, ct);
        var name = user is not null && user.TenantId == tenantId ? $"{user.Name} {user.LastName}".Trim() : "";
        return new OffboardingPersonResponse(id, string.IsNullOrWhiteSpace(name) ? "Support team" : name);
    }
}
