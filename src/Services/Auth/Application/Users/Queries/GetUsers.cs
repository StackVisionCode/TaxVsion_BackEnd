using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users.Queries;

public sealed record UserSummaryResponse(
    Guid Id,
    string Name,
    string LastName,
    string Email,
    string ActorType,
    bool IsActive,
    // Ciclo de vida: Active/Deactivated/Offboarded (viaja como string). Distingue un retiro terminal
    // de una simple suspensión, que IsActive por sí solo no puede.
    string Status,
    bool MfaEnabled,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles,
    // Solo tiene valor en usuarios de portal de cliente (ActorType CustomerPortal); permite al CRM
    // correlacionar un usuario del portal con el cliente de su perfil. Ver el filtro `CustomerId`.
    Guid? CustomerId
);

public sealed record GetUsersQuery(
    Guid TenantId,
    int Page = 1,
    int Size = 20,
    string? Search = null,
    bool? IsActive = null,
    Guid? CustomerId = null
);

public static class GetUsersHandler
{
    public static async Task<Result<PagedResult<UserSummaryResponse>>> Handle(
        GetUsersQuery query,
        IUserRepository users,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
        {
            return Result.Failure<PagedResult<UserSummaryResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100.")
            );
        }

        var (items, total) = await users.GetPagedAsync(
            query.TenantId,
            query.Page,
            query.Size,
            query.Search,
            query.IsActive,
            query.CustomerId,
            ct
        );

        var responses = new List<UserSummaryResponse>(items.Count);
        foreach (var user in items)
        {
            var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
            // Solo los roles asignados reales (Role.Name). No se incluye user.Roles porque ahí el
            // actor_type viaja como pseudo-rol (UserActorRoles.For → "CustomerPortal") y se duplicaría
            // con el nombre del rol de sistema ("Customer Portal"); el actor_type ya va en ActorType.
            var roleNames = userRoles.Where(role => role.IsActive).Select(role => role.Name).ToList();

            responses.Add(
                new UserSummaryResponse(
                    user.Id,
                    user.Name,
                    user.LastName,
                    user.Email,
                    user.ActorType.ToString(),
                    user.IsActive,
                    user.Status.ToString(),
                    user.MfaEnabled,
                    user.CreatedAtUtc,
                    roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    user.CustomerId
                )
            );
        }

        return Result.Success(new PagedResult<UserSummaryResponse>(responses, query.Page, query.Size, total));
    }
}

public sealed record GetUserByIdQuery(Guid TenantId, Guid UserId);

public static class GetUserByIdHandler
{
    public static async Task<Result<UserSummaryResponse>> Handle(
        GetUserByIdQuery query,
        IUserRepository users,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || user.TenantId != query.TenantId)
        {
            return Result.Failure<UserSummaryResponse>(
                new Error("User.NotFound", "User does not exist in this tenant.")
            );
        }

        var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
        // Solo roles reales (ver GetUsersHandler): user.Roles trae el actor_type como pseudo-rol y se
        // duplicaría con el rol de sistema; el actor_type ya va en ActorType.
        var roleNames = userRoles.Where(role => role.IsActive).Select(role => role.Name).ToList();

        return Result.Success(
            new UserSummaryResponse(
                user.Id,
                user.Name,
                user.LastName,
                user.Email,
                user.ActorType.ToString(),
                user.IsActive,
                user.Status.ToString(),
                user.MfaEnabled,
                user.CreatedAtUtc,
                roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                user.CustomerId
            )
        );
    }
}
