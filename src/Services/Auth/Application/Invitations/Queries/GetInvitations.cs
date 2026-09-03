using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;

namespace TaxVision.Auth.Application.Invitations.Queries;

public sealed record InvitationResponse(
    Guid Id,
    string Email,
    string ActorType,
    string Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    int ResendCount,
    DateTime? LastSentAtUtc,
    Guid? InvitedByUserId,
    // Solo tiene valor en invitaciones de portal de cliente (ActorType CustomerPortal); permite al
    // CRM correlacionar una invitación con el cliente de su perfil. Ver el filtro `CustomerId` abajo.
    Guid? CustomerId
);

public sealed record GetInvitationsQuery(
    Guid TenantId,
    InvitationStatus? Status = null,
    int Page = 1,
    int Size = 20,
    Guid? CustomerId = null
);

public static class GetInvitationsHandler
{
    public static async Task<Result<PagedResult<InvitationResponse>>> Handle(
        GetInvitationsQuery query,
        IInvitationRepository invitations,
        CancellationToken ct
    )
    {
        if (query.Page < 1 || query.Size is < 1 or > 100)
        {
            return Result.Failure<PagedResult<InvitationResponse>>(
                new Error("Query.Pagination", "Page must be >= 1 and size between 1 and 100.")
            );
        }

        var (items, total) = await invitations.GetPagedAsync(
            query.TenantId,
            query.Status,
            query.Page,
            query.Size,
            query.CustomerId,
            ct
        );

        IReadOnlyList<InvitationResponse> responses = items
            .Select(invitation => new InvitationResponse(
                invitation.Id,
                invitation.Email,
                invitation.ActorType.ToString(),
                invitation.Status.ToString(),
                invitation.CreatedAtUtc,
                invitation.ExpiresAtUtc,
                invitation.ResendCount,
                invitation.LastSentAtUtc,
                invitation.InvitedByUserId,
                invitation.CustomerId
            ))
            .ToList();

        return Result.Success(new PagedResult<InvitationResponse>(responses, query.Page, query.Size, total));
    }
}
