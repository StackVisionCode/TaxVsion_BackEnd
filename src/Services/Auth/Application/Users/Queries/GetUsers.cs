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
    bool MfaEnabled,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles
);

public sealed record GetUsersQuery(
    Guid TenantId,
    int Page = 1,
    int Size = 20,
    string? Search = null,
    bool? IsActive = null
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
            ct
        );

        var responses = new List<UserSummaryResponse>(items.Count);
        foreach (var user in items)
        {
            var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
            var roleNames = new List<string>(user.Roles);
            roleNames.AddRange(userRoles.Where(role => role.IsActive).Select(role => role.Name));

            responses.Add(
                new UserSummaryResponse(
                    user.Id,
                    user.Name,
                    user.LastName,
                    user.Email,
                    user.ActorType.ToString(),
                    user.IsActive,
                    user.MfaEnabled,
                    user.CreatedAtUtc,
                    roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
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
        var roleNames = new List<string>(user.Roles);
        roleNames.AddRange(userRoles.Where(role => role.IsActive).Select(role => role.Name));

        return Result.Success(
            new UserSummaryResponse(
                user.Id,
                user.Name,
                user.LastName,
                user.Email,
                user.ActorType.ToString(),
                user.IsActive,
                user.MfaEnabled,
                user.CreatedAtUtc,
                roleNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            )
        );
    }
}
