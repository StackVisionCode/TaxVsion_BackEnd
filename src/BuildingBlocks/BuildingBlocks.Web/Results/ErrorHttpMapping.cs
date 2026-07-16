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
            or "SaaSPayment.NotFound"
            or "TenantProviderCustomer.NotFound"
            or "TenantProviderCustomer.MethodNotFound"
            or "TenantPaymentConfig.NotFound"
            or "TenantPayment.NotFound"
            or "PaymentLink.NotFound"
            or "TenantConnectAccount.NotFound"
            or "PayoutSchedule.NotFound"
            or "TenantRecurringPayment.NotFound"
            or "TenantRecurringPayment.ScheduleNotFound" => StatusCodes.Status404NotFound,
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
            or "TenantPaymentConfig.NotActive"
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
            or "EmailAccount.Forbidden" => StatusCodes.Status403Forbidden,
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
            or "TenantPaymentConfig.AlreadyExists" => StatusCodes.Status409Conflict,
            "Auth.LockedOut" or "Auth.OtpThrottled" or "Invitation.ResendLimit"
            or "PaymentApp.AdminActionThrottled"
            or "PaymentLink.RedemptionThrottled" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        };
}
