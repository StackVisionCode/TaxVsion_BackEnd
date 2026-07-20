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
            or "Folder.NotFound"
            or "EmailConfiguration.NotFound"
            or "EmailTemplate.NotFound"
            or "EmailLayout.NotFound"
            or "EmailTemplateVersion.NotFound"
            or "EmailLayoutVersion.NotFound"
            or "EmailTemplate.VersionNotFound"
            or "EmailLayout.VersionNotFound"
            or "EmailRenderer.NoMapping"
            or "EmailRenderer.NoPublishedVersion"
            or "EmailRenderer.LayoutVersionNotFound"
            or "Campaign.NotFound"
            or "EmailAccount.NotFound"
            or "EmailMessage.NotFound"
            or "TenantDomain.NotFound"
            or "ShareLink.NotFound"
            or "TenantEmailAccount.NotFound"
            or "ProviderWatchSubscription.NotFound"
            or "GetMessageAttachmentHandler.AttachmentNotFound"
            or "IncomingEmail.NotFound"
            or "IncomingEmailAttachment.NotFound"
            or "EmailThread.NotFound"
            or "Draft.NotFound"
            or "EventTemplateMapping.NotFound"
            or "Tenant.Logo.NotFound"
            // Fase 16.5 (hardening Postmaster): los 4 códigos NotFound propios de Postmaster no
            // tenían entrada explícita y caían al default (400) en vez del 404 semánticamente
            // correcto — mismo gap exacto que EventTemplateMapping.NotFound tuvo en Scribe Fase 10.5.
            or "SentMessage.NotFound"
            or "TenantEmailProvider.NotFound"
            or "SystemEmailProvider.NotFound"
            or "SuppressionListEntry.NotFound"
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
            or "Auth.TenantIdRequired"
            // EventTemplateMapping.{Tenant,TenantRequired,TenantNotAllowed} son validación de
            // contexto de tenant (falta contexto / contexto no permitido para el Scope), no un
            // problema de autorización — mismo tratamiento que sus equivalentes EmailTemplate.*/
            // EmailLayout.* (Tenant/TenantRequired/TenantNotAllowed), que tampoco están mapeados
            // explícitamente en este archivo y por eso ya caen correctamente en el "default" 400
            // de más abajo. Se agregan acá explícitos (mismo resultado, 400) en vez de dejarlos
            // caer al default, para que los 5 códigos de EventTemplateMapping quedan documentados
            // 1:1 en este mapping — a diferencia de NotFound/Forbidden, NO se mapean a 403: hacerlo
            // sería una regresión real de semántica HTTP (error de validación de payload, no de
            // autorización) y una desviación del comportamiento ya establecido para sus
            // equivalentes de Templates/Layouts.
            or "EventTemplateMapping.Tenant"
            or "EventTemplateMapping.TenantRequired"
            or "EventTemplateMapping.TenantNotAllowed" => StatusCodes.Status400BadRequest,
            "TenantDomain.ReservationConsumed"
            or "TenantDomain.ReservationExpired"
            or "TenantDomain.HostTaken"
            or "TenantDomain.SlugTaken"
            or "TenantDomain.SlugReservedTemporarily"
            or "TenantDomain.NotReadyForActivation"
            or "TenantEmailAccount.InvalidTransition"
            or "IncomingEmailAttachment.InvalidTransition"
            or "IncomingEmailAttachment.NotReady"
            or "Draft.InvalidTransition" => StatusCodes.Status409Conflict,
            "TenantDomain.Disabled"
            or "TenantDomain.PrimaryCannotBeDisabled"
            or "SetupWatchHandler.Forbidden"
            or "GetMessageBodyHandler.Forbidden"
            or "GetMessageAttachmentHandler.Forbidden"
            or "SendMessageHandler.Forbidden"
            or "SendMessageHandler.PermissionDenied"
            or "SendCorrespondenceMessageHandler.AccountNotFound" => StatusCodes.Status403Forbidden,
            "TenantDomain.CloudflareHttp"
            or "TenantDomain.CloudflareRejected"
            or "SetupWatchHandler.ProviderFailed"
            or "WatchRenewalService.RenewalFailed"
            or "WatchRenewalService.SubscriptionExpired"
            or "GetMessageBodyHandler.ProviderFailed"
            or "GetMessageAttachmentHandler.ProviderFailed"
            or "SendMessageHandler.TransientProviderError"
            or "SendCorrespondenceMessageHandler.ConnectorsSendFailed"
            or "SendCorrespondenceMessageHandler.AttachmentFetchFailed"
            or "ConnectorsClient.EmptyResponse"
            or "ConnectorsClient.UnexpectedStatus"
            or "CloudStorageClient.EmptyResponse"
            or "CloudStorageClient.UnexpectedStatus"
            or "CorrespondenceTempBucketUploader.UploadFailed"
            or "PostmasterClient.EmptyResponse"
            or "PostmasterClient.UnexpectedStatus"
            or "Tenant.Logo.Storage.Upload"
            or "Tenant.Logo.Storage.Download"
            or "Tenant.Logo.Storage.Delete" => StatusCodes.Status502BadGateway,
            "ConnectorsClient.ServiceAuthUnavailable"
            or "ConnectorsClient.Unavailable"
            or "ConnectorsClient.RequestFailed"
            or "CloudStorageClient.ServiceAuthUnavailable"
            or "CloudStorageClient.RequestFailed"
            or "PostmasterClient.ServiceAuthUnavailable"
            or "PostmasterClient.RequestFailed"
            or "Tenant.Logo.Storage.Auth" => StatusCodes.Status503ServiceUnavailable,
            "GetMessageBodyHandler.Timeout" or "GetMessageAttachmentHandler.Timeout" or "SendMessageHandler.Timeout" =>
                StatusCodes.Status504GatewayTimeout,
            "GetMessageBodyHandler.RateLimited"
            or "GetMessageAttachmentHandler.RateLimited"
            or "SendMessageHandler.RateLimited"
            or "SendMessageHandler.QuotaExceeded" => StatusCodes.Status429TooManyRequests,
            "SendMessageHandler.AuthExpired" => StatusCodes.Status401Unauthorized,
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
            or "Folder.Forbidden"
            or "EmailConfiguration.Forbidden"
            or "EmailTemplate.Forbidden"
            or "EmailLayout.Forbidden"
            or "EventTemplateMapping.Forbidden"
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
            or "ShareLink.AlreadyRevoked"
            or "TenantPaymentConfig.AlreadyExists"
            // 2026-07-20 — DeleteFolderHandler: la carpeta tiene subfolders o archivos directos,
            // el llamador debe vaciarla primero. Sin esta entrada caía al default 400.
            or "Folder.NotEmpty" => StatusCodes.Status409Conflict,
            "Auth.LockedOut"
            or "Auth.OtpThrottled"
            or "Invitation.ResendLimit"
            or "PaymentApp.AdminActionThrottled"
            or "PaymentLink.RedemptionThrottled" => StatusCodes.Status429TooManyRequests,
            "File.TooManyItems" or "File.ZipTooLarge" or "File.TooManyFolders" or "File.TooLarge" =>
                StatusCodes.Status413PayloadTooLarge,
            "File.MultipartCompleteFailed"
            or "SendCorrespondenceMessageHandler.AllRecipientsSuppressed"
            or "SendCorrespondenceMessageHandler.SendInProgress" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
}
