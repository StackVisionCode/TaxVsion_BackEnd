namespace TaxVision.Notification.Domain.Catalog;

public static class NotificationKinds
{
    public const string CloudStorageFileBlockedByPolicy = "cloudstorage.file.blocked_by_policy";
    public const string CloudStorageFilePendingReview = "cloudstorage.file.pending_review";
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

    /// <summary>Solo lo emite Communication (Node): el cliente que firmó ve su firma registrada.</summary>
    public const string SignatureDocumentSignedCustomerSigner = "signature.document.signed.customer_signer";
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
    public const string CorrespondenceDeliveryFailed = "correspondence.delivery_failed";

    public const string TasksTaskAssigned = "tasks.task_assigned";
    public const string TasksTaskOverdue = "tasks.task_overdue";
    public const string TasksAttachmentRejected = "tasks.attachment_rejected";
    public const string TasksClientRequestCreated = "tasks.client_request_created";
    public const string TasksClientRequestFulfilled = "tasks.client_request_fulfilled";
    public const string TasksClientRequestDocumentRejected = "tasks.client_request_document_rejected";
    public const string TasksWaitingOnClient = "tasks.waiting_on_client";
    public const string TasksFormRequestAssigned = "tasks.form_request_assigned";
    public const string TasksFormRequestSubmitted = "tasks.form_request_submitted";
    public const string TasksFormRequestReturned = "tasks.form_request_returned";
    public const string TasksFormRequestCompleted = "tasks.form_request_completed";
    public const string TasksFormRequestCancelled = "tasks.form_request_cancelled";
    public const string TasksFormAttachmentRejected = "tasks.form_attachment_rejected";
    public const string TasksFormAttachmentRejectedStaff = "tasks.form_attachment_rejected_staff";
    public const string TasksFormRequestReminder = "tasks.form_request_reminder";
    public const string TasksFormAccessLink = "tasks.form_access_link";
    public const string TasksFormAccessCode = "tasks.form_access_code";
    public const string TasksFormActionFailed = "tasks.form_action_failed";
    public const string TasksFormParticipantReturned = "tasks.form_participant_returned";
    public const string TasksFormParticipantReminder = "tasks.form_participant_reminder";
    public const string TasksFormParticipantCompleted = "tasks.form_participant_completed";
    public const string TasksFormParticipantCancelled = "tasks.form_participant_cancelled";
    public const string TasksFormParticipantAttachmentRejected = "tasks.form_participant_attachment_rejected";
    public const string RemindersDue = "reminders.due";

    public const string CalendarAppointmentScheduled = "calendar.appointment_scheduled";
    public const string CalendarAttendeeAdded = "calendar.attendee_added";
    public const string CalendarAppointmentUpdated = "calendar.appointment_updated";
    public const string CalendarAppointmentBookedOnline = "calendar.appointment_booked_online";
    public const string CalendarBookingRequested = "calendar.booking_requested";
    public const string CalendarBookingDeclined = "calendar.booking_declined";
    public const string CalendarBookingConfirmed = "calendar.booking_confirmed";
    public const string CalendarAppointmentRescheduled = "calendar.appointment_rescheduled";
    public const string CalendarAppointmentCancelled = "calendar.appointment_cancelled";
    public const string CalendarAttendeeResponded = "calendar.attendee_responded";
    public const string CalendarAppointmentStartingSoon = "calendar.appointment_starting_soon";

    public const string CommunicationMeetingInvitationCreated = "communication.meeting.invitation_created";
    public const string CommunicationMeetingInvitationRevoked = "communication.meeting.invitation_revoked";
    public const string CommunicationMeetingCancelled = "communication.meeting.cancelled";
    public const string CommunicationMeetingRescheduled = "communication.meeting.rescheduled";
    public const string CommunicationMeetingReminder = "communication.meeting.reminder";
    public const string CommunicationCallIncoming = "communication.call.incoming";
    public const string CommunicationChatMessage = "communication.chat.message";
    public const string CommunicationChatMessagesMissed = "communication.chat.messages_missed";

    /// <summary>Solo lo emite Communication (Node): alguien te agregó a un grupo de chat.</summary>
    public const string CommunicationChatGroupMemberAdded = "communication.chat.group_member_added";
    public const string CommunicationCallMissed = "communication.call.missed";
    public const string CommunicationCallRecordingReady = "communication.call.recording_ready";
    public const string CommunicationCallRecordingFailed = "communication.call.recording_failed";
    public const string CommunicationMeetingRecordingReady = "communication.meeting.recording_ready";
    public const string CommunicationMeetingRecordingFailed = "communication.meeting.recording_failed";
    public const string CommunicationSupportOpened = "communication.support.opened";
    public const string CommunicationSupportMessageReceived = "communication.support.message_received";
    public const string CommunicationSupportReplyReceived = "communication.support.reply_received";
    public const string CommunicationSupportClaimed = "communication.support.claimed";
    public const string CommunicationSupportResolved = "communication.support.resolved";
    public const string CommunicationSupportClosed = "communication.support.closed";

    public const string AuthSecurityAlert = "auth.security_alert";
    public const string AuthUserSlotGated = "auth.user_slot_gated";
    public const string AuthUserSlotUnlocked = "auth.user_slot_unlocked";

    public const string SubscriptionStatusChanged = "subscription.status_changed";
    public const string SubscriptionRenewalUpcoming = "subscription.renewal_upcoming";

    /// <summary>Un descuento de N meses termina en 7 días (aviso y correo al owner).</summary>
    public const string SubscriptionDiscountEnding = "subscription.discount_ending";
    public const string PaymentAppPaymentMethodExpiringSoon = "payment_app.payment_method_expiring_soon";
    public const string PaymentClientTenantPaymentSucceeded = "payment_client.tenant_payment_succeeded";

    /// <summary>Alguien se registró con el código de referido de la oficina (aviso al owner).</summary>
    public const string ReferralAttributed = "growth.referral_attributed";

    /// <summary>El referido pagó su plan: el premio queda en espera (aviso al owner).</summary>
    public const string ReferralQualified = "growth.referral_qualified";

    /// <summary>El premio de referido llegó al Wallet (aviso y correo al owner).</summary>
    public const string ReferralRewardGranted = "growth.referral_reward_granted";

    /// <summary>El premio se canceló antes de acreditarse (reembolso, disputa perdida o revisión).</summary>
    public const string ReferralRewardRejected = "growth.referral_reward_rejected";

    /// <summary>El premio ya acreditado se quitó del Wallet.</summary>
    public const string ReferralRewardReversed = "growth.referral_reward_reversed";

    /// <summary>El saldo del Wallet bajó del umbral (correo y campana a wallet.manage).</summary>
    public const string WalletLowBalance = "wallet.low_balance";

    /// <summary>Recibo de una recarga automática del Wallet.</summary>
    public const string WalletAutoRechargeSucceeded = "wallet.auto_recharge_succeeded";

    /// <summary>La recarga automática quedó en pausa (cobro fallido o método no disponible).</summary>
    public const string WalletAutoRechargePaused = "wallet.auto_recharge_paused";

    /// <summary>Se llegó al tope diario de recargas automáticas; sigue al día siguiente.</summary>
    public const string WalletAutoRechargeDailyLimitReached = "wallet.auto_recharge_daily_limit";
    public const string PaymentClientPayoutFailed = "payment_client.payout_failed";
    public const string PaymentClientConnectAccountRestricted = "payment_client.connect_account_restricted";
    public const string PaymentClientConnectAccountOnboardingRequired =
        "payment_client.connect_account_onboarding_required";
    public const string BillingInvoiceSentToCustomer = "billing.invoice_sent_to_customer";

    /// <summary>Aviso AL PERSONAL de la oficina: una factura quedó pagada (a diferencia del anterior, que es para el cliente).</summary>
    public const string BillingInvoicePaid = "billing.invoice_paid";

    /// <summary>Aviso AL PERSONAL: un cobro confirmado no se pudo aplicar a su factura y quedó pendiente de resolver.</summary>
    public const string BillingInvoicePaymentRejected = "billing.invoice_payment_rejected";
    public const string DocumentsGenerationFailed = "documents.generation_failed";
    public const string TenantWispReviewDue = "tenant.wisp_review_due";

    // Avisos del retiro de empleados (offboard).
    public const string TenantSecurityCoordinatorVacated = "tenant.security_coordinator_vacated";
    public const string CampaignsSchedulesPausedOnOffboarding = "campaigns.schedules_paused_on_offboarding";
    public const string SignaturePreparerAttentionRequired = "signature.preparer.attention_required";
    public const string SignaturePreparerChanged = "signature.preparer.changed";
    public const string CloudStorageConnectionReviewRequired = "cloudstorage.connection.review_required";
    public const string ConnectorsOfficeMailboxReviewRequired = "connectors.office_mailbox.review_required";
    public const string AuthOffboardingIncomplete = "auth.offboarding.incomplete";

    // Financial Insights: lote de estados de cuenta terminado.
    public const string FinancialImportBatchCompleted = "financial.import_batch.completed";
    public const string FinancialPortalImportBatchCompleted = "financial.portal_import_batch.completed";
    public const string FinancialPortalImportBatchSubmitted = "financial.portal_import_batch.submitted";
}
