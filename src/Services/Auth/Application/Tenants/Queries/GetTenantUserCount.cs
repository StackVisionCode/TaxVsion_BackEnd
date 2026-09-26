using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.Queries;

/// <summary>Cuánta gente ocupa cupo hoy en la oficina: activos más invitaciones sin aceptar.</summary>
public sealed record TenantUserCountResponse(int ActiveUsers, int PendingInvitations);

public sealed record GetTenantUserCountQuery(Guid TenantId);

/// <summary>
/// Solo Auth sabe cuántos usuarios activos hay. Lo consulta Subscription por M2M antes de agendar un
/// downgrade, para no dejar a la oficina por encima del cupo del plan destino. Es el mismo conteo que
/// <see cref="GetTenantLimitsHandler"/> expone al front, sin los datos del plan.
/// </summary>
public static class GetTenantUserCountHandler
{
    public static async Task<Result<TenantUserCountResponse>> Handle(
        GetTenantUserCountQuery query,
        IUserRepository users,
        IInvitationRepository invitations,
        CancellationToken ct
    )
    {
        var activeUsers = await users.CountActiveAsync(query.TenantId, ct);
        var pending = await invitations.CountPendingAsync(query.TenantId, ct);

        return Result.Success(new TenantUserCountResponse(activeUsers, pending));
    }
}
