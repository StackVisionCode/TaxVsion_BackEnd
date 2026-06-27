using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Results;

public static class ErrorHttpMapping
{
    public static int ToHttpStatusCode(this Error error) =>
        error.Code switch
        {
            "Tenant.NotFound" or "Invitation.NotFound" => StatusCodes.Status404NotFound,
            "Auth.Invalid" or "Auth.InvalidInvitation" or "Auth.InvalidRefreshToken" =>
                StatusCodes.Status401Unauthorized,
            "Auth.Inactive" or "Tenant.Inactive" or "Invitation.Forbidden" =>
                StatusCodes.Status403Forbidden,
            "Tenant.SubdomainConflict" or
                "User.EmailConflict" or
                "Invitation.PendingConflict" =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
}
