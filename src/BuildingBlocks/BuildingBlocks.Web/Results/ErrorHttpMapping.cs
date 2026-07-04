using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Results;

public static class ErrorHttpMapping
{
    public static int ToHttpStatusCode(this Error error) =>
        error.Code switch
        {
            "Tenant.NotFound"
            or "Invitation.NotFound"
            or "User.NotFound"
            or "Role.NotFound"
            or "Session.NotFound"
            or "Permission.NotFound"
            or "Mfa.DeviceNotFound"
            or "File.NotFound" => StatusCodes.Status404NotFound,
            "Auth.Invalid"
            or "Auth.InvalidInvitation"
            or "Auth.InvalidRefreshToken"
            or "Auth.InvalidResetToken"
            or "Auth.InvalidVerificationToken"
            or "Auth.InvalidVerificationCode"
            or "Auth.MfaInvalid"
            or "Auth.SessionRevoked" => StatusCodes.Status401Unauthorized,
            "Auth.Inactive"
            or "Tenant.Inactive"
            or "Invitation.Forbidden"
            or "Session.Forbidden"
            or "Mfa.RequiredByPolicy"
            or "Auth.StepUpRequired"
            or "Subscription.Suspended"
            or "StorageQuota.Suspended"
            or "File.NotAvailable"
            or "File.Forbidden" => StatusCodes.Status403Forbidden,
            "Tenant.SubdomainConflict"
            or "User.EmailConflict"
            or "Invitation.PendingConflict"
            or "Role.NameConflict"
            or "Plan.UserLimitReached"
            or "Plan.InvitationLimitReached"
            or "Mfa.AlreadyEnabled"
            or "StorageQuota.Exceeded" => StatusCodes.Status409Conflict,
            "Auth.LockedOut" or "Auth.OtpThrottled" or "Invitation.ResendLimit" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        };
}
