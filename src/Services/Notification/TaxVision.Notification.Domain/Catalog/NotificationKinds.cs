namespace TaxVision.Notification.Domain.Catalog;

public static class NotificationKinds
{
    public const string CloudStorageFileBlockedByPolicy = "cloudstorage.file.blocked_by_policy";
    public const string CloudStorageFileInfected = "cloudstorage.file.infected";
    public const string CloudStorageStorageLimitExceeded = "cloudstorage.storage_limit_exceeded";
    public const string CloudStorageFolderItemAdded = "cloudstorage.sharelink.folder_item_added";
    public const string CloudStorageDmcaTakedown = "cloudstorage.file.blocked_by_dmca_takedown";
    public const string CloudStorageDmcaReinstated = "cloudstorage.file.reinstated_from_takedown";
    public const string CloudStorageLegalHoldPlaced = "cloudstorage.file.legal_hold_placed";
    public const string CloudStorageLegalHoldLifted = "cloudstorage.file.legal_hold_lifted";
    public const string CloudStorageDmcaCounterNoticeSubmitted = "cloudstorage.dmca.counter_notice_submitted";
    public const string CloudStorageCustomerDocumentUploaded = "cloudstorage.customer_document_uploaded";

    public const string SignatureSignerInvited = "signature.signer.invited";
    public const string SignatureDocumentSigned = "signature.document.signed";
    public const string SignatureRequestCompleted = "signature.request.completed";
    public const string SignatureSignerRejected = "signature.signer.rejected";
    public const string SignatureRequestExpired = "signature.request.expired";
    public const string SignatureSignerVerificationFailed = "signature.signer.verification_failed";
    public const string SignatureSignerPinFailed = "signature.signer.pin_failed";
    public const string SignatureRequestSealingFailed = "signature.request.sealing_failed";
    public const string SignatureRequestCanceledByAnother = "signature.request.canceled_by_another";

    public const string CustomerBulkImported = "customer.bulk_imported";
    public const string CustomerImportFailed = "customer.import_failed";
    public const string ConnectorsOAuthRefreshFailed = "connectors.oauth.refresh_failed";
    public const string ConnectorsWatchExpired = "connectors.watch.expired";
    public const string CorrespondenceCustomerEmailReceived = "correspondence.customer_email_received";

    public const string TasksTaskAssigned = "tasks.task_assigned";
    public const string TasksTaskOverdue = "tasks.task_overdue";
    public const string TasksAttachmentRejected = "tasks.attachment_rejected";
    public const string TasksClientRequestCreated = "tasks.client_request_created";
    public const string TasksClientRequestFulfilled = "tasks.client_request_fulfilled";
    public const string TasksClientRequestDocumentRejected = "tasks.client_request_document_rejected";
    public const string TasksWaitingOnClient = "tasks.waiting_on_client";
    public const string RemindersDue = "reminders.due";

    public const string CalendarAppointmentScheduled = "calendar.appointment_scheduled";
    public const string CalendarAttendeeAdded = "calendar.attendee_added";
    public const string CalendarAppointmentRescheduled = "calendar.appointment_rescheduled";
    public const string CalendarAppointmentCancelled = "calendar.appointment_cancelled";
    public const string CalendarAttendeeResponded = "calendar.attendee_responded";
    public const string CalendarAppointmentStartingSoon = "calendar.appointment_starting_soon";

    public const string CommunicationMeetingInvitationCreated = "communication.meeting.invitation_created";
    public const string CommunicationCallMissed = "communication.call.missed";
    public const string CommunicationCallRecordingReady = "communication.call.recording_ready";
    public const string CommunicationCallRecordingFailed = "communication.call.recording_failed";
    public const string CommunicationMeetingRecordingReady = "communication.meeting.recording_ready";
    public const string CommunicationMeetingRecordingFailed = "communication.meeting.recording_failed";
    public const string CommunicationSupportOpened = "communication.support.opened";

    public const string AuthSecurityAlert = "auth.security_alert";
    public const string AuthUserSlotGated = "auth.user_slot_gated";
    public const string AuthUserSlotUnlocked = "auth.user_slot_unlocked";

    public const string SubscriptionStatusChanged = "subscription.status_changed";
    public const string SubscriptionRenewalUpcoming = "subscription.renewal_upcoming";
    public const string PaymentAppPaymentMethodExpiringSoon = "payment_app.payment_method_expiring_soon";
    public const string PaymentClientTenantPaymentSucceeded = "payment_client.tenant_payment_succeeded";
    public const string PaymentClientPayoutFailed = "payment_client.payout_failed";
    public const string PaymentClientConnectAccountRestricted = "payment_client.connect_account_restricted";
    public const string PaymentClientConnectAccountOnboardingRequired =
        "payment_client.connect_account_onboarding_required";
    public const string BillingInvoiceSentToCustomer = "billing.invoice_sent_to_customer";
    public const string DocumentsGenerationFailed = "documents.generation_failed";
}
