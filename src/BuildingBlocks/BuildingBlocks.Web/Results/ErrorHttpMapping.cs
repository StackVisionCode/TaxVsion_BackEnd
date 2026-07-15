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
            or "File.NotFound"
            or "EmailConfiguration.NotFound"
            or "EmailTemplate.NotFound"
            or "EmailLayout.NotFound"
            or "Campaign.NotFound"
            or "EmailAccount.NotFound"
            or "EmailMessage.NotFound"
            or "TenantDomain.NotFound"
            or "ShareLink.NotFound" => StatusCodes.Status404NotFound,
            "TenantDomain.SlugLength"
            or "TenantDomain.SlugInvalid"
            or "TenantDomain.SlugReserved"
            or "TenantDomain.Tenant"
            or "TenantDomain.BaseDomain"
            or "TenantDomain.HostInvalid"
            or "TenantDomain.NotCustomHostname"
            or "TenantDomain.NotSubdomain"
            or "TenantDomain.SlugUnchanged"
            or "TenantDomain.InvalidTransition"
            or "TenantDomain.ReservationEmail"
            or "TenantDomain.ReservationTtl"
            or "Auth.TenantIdRequired" => StatusCodes.Status400BadRequest,
            "TenantDomain.ReservationConsumed"
            or "TenantDomain.ReservationExpired"
            or "TenantDomain.HostTaken"
            or "TenantDomain.SlugTaken"
            or "TenantDomain.SlugReservedTemporarily"
            or "TenantDomain.NotReadyForActivation" => StatusCodes.Status409Conflict,
            "TenantDomain.Disabled"
            or "TenantDomain.PrimaryCannotBeDisabled" => StatusCodes.Status403Forbidden,
            "TenantDomain.CloudflareHttp" or "TenantDomain.CloudflareRejected" => StatusCodes.Status502BadGateway,
            "Auth.Invalid"
            or "Auth.InvalidInvitation"
            or "Auth.InvalidRefreshToken"
            or "Auth.InvalidResetToken"
            or "Auth.InvalidVerificationToken"
            or "Auth.InvalidVerificationCode"
            or "Auth.MfaInvalid"
            or "Auth.SessionRevoked"
            or "Auth.InvalidClient" => StatusCodes.Status401Unauthorized,
            "Auth.Inactive"
            or "Tenant.Inactive"
            or "Invitation.Forbidden"
            or "Session.Forbidden"
            or "Mfa.RequiredByPolicy"
            or "Auth.StepUpRequired"
            or "Subscription.Suspended"
            or "StorageQuota.Suspended"
            or "File.NotAvailable"
            or "File.Forbidden"
            or "EmailConfiguration.Forbidden"
            or "EmailTemplate.Forbidden"
            or "EmailLayout.Forbidden"
            or "Campaign.Forbidden"
            or "EmailAccount.Forbidden"
            or "Role.PermissionNotAssignable"
            or "Role.NotAssignableToCustomerPortal"
            or "ShareLink.Forbidden"
            or "ShareLink.PublicSharingDisabled"
            or "ShareLink.ElevatedPermissionRequiresManage" => StatusCodes.Status403Forbidden,
            "Tenant.SubdomainConflict"
            or "User.EmailConflict"
            or "Invitation.PendingConflict"
            or "Role.NameConflict"
            or "Plan.UserLimitReached"
            or "Plan.InvitationLimitReached"
            or "Mfa.AlreadyEnabled"
            or "StorageQuota.Exceeded"
            or "EmailConfiguration.Conflict"
            or "EmailTemplate.KeyConflict"
            or "EmailLayout.NameConflict"
            or "EmailAccount.Conflict"
            or "ShareLink.AlreadyRevoked" => StatusCodes.Status409Conflict,
            "Auth.LockedOut" or "Auth.OtpThrottled" or "Invitation.ResendLimit" => StatusCodes.Status429TooManyRequests,
            "File.TooManyItems" or "File.ZipTooLarge" => StatusCodes.Status413PayloadTooLarge,
            "File.MultipartCompleteFailed" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
}
