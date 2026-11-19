namespace TaxVision.Tenant.Domain.Team;

/// <summary>
/// Verbos v1 del historial (contrato con el CRM, no se renombran). Cada verbo fija su tipo de recurso.
/// </summary>
public static class ActivityVerbs
{
    public const string TaskCreated = "task.created";
    public const string TaskCompleted = "task.completed";
    public const string TaskReopened = "task.reopened";
    public const string TaskCancelled = "task.cancelled";
    public const string ClientRequestCreated = "client_request.created";
    public const string FormRequestAssigned = "form_request.assigned";
    public const string FormRequestCompleted = "form_request.completed";
    public const string FormRequestReturned = "form_request.returned";
    public const string AppointmentScheduled = "appointment.scheduled";
    public const string AppointmentRescheduled = "appointment.rescheduled";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string NoteCreated = "note.created";
    public const string CustomerCreated = "customer.created";
    public const string CustomerPreparerAssigned = "customer.preparer_assigned";
    public const string SignatureCreated = "signature.created";
    public const string SignatureSent = "signature.sent";
    public const string SignaturePreparerSigned = "signature.preparer_signed";
    public const string SignatureCanceled = "signature.canceled";
    public const string InvoiceIssued = "invoice.issued";
    public const string InvoicePaymentRecorded = "invoice.payment_recorded";
    public const string InvoiceVoided = "invoice.voided";
    public const string InvoiceSent = "invoice.sent";
    public const string SmsSent = "sms.sent";
    public const string EmailSent = "email.sent";
    public const string EmailReplied = "email.replied";
    public const string DocumentUploaded = "document.uploaded";
    public const string SupportResolved = "support.resolved";
    public const string SupportClosed = "support.closed";

    private static readonly Dictionary<string, string> ResourceTypes = new(StringComparer.Ordinal)
    {
        [TaskCreated] = ActivityResourceTypes.Task,
        [TaskCompleted] = ActivityResourceTypes.Task,
        [TaskReopened] = ActivityResourceTypes.Task,
        [TaskCancelled] = ActivityResourceTypes.Task,
        [ClientRequestCreated] = ActivityResourceTypes.ClientRequest,
        [FormRequestAssigned] = ActivityResourceTypes.FormRequest,
        [FormRequestCompleted] = ActivityResourceTypes.FormRequest,
        [FormRequestReturned] = ActivityResourceTypes.FormRequest,
        [AppointmentScheduled] = ActivityResourceTypes.Appointment,
        [AppointmentRescheduled] = ActivityResourceTypes.Appointment,
        [AppointmentCancelled] = ActivityResourceTypes.Appointment,
        [NoteCreated] = ActivityResourceTypes.Note,
        [CustomerCreated] = ActivityResourceTypes.Customer,
        [CustomerPreparerAssigned] = ActivityResourceTypes.Customer,
        [SignatureCreated] = ActivityResourceTypes.SignatureRequest,
        [SignatureSent] = ActivityResourceTypes.SignatureRequest,
        [SignaturePreparerSigned] = ActivityResourceTypes.SignatureRequest,
        [SignatureCanceled] = ActivityResourceTypes.SignatureRequest,
        [InvoiceIssued] = ActivityResourceTypes.Invoice,
        [InvoicePaymentRecorded] = ActivityResourceTypes.Invoice,
        [InvoiceVoided] = ActivityResourceTypes.Invoice,
        [InvoiceSent] = ActivityResourceTypes.Invoice,
        [SmsSent] = ActivityResourceTypes.SmsMessage,
        [EmailSent] = ActivityResourceTypes.EmailMessage,
        [EmailReplied] = ActivityResourceTypes.EmailThread,
        [DocumentUploaded] = ActivityResourceTypes.Document,
        [SupportResolved] = ActivityResourceTypes.SupportTicket,
        [SupportClosed] = ActivityResourceTypes.SupportTicket,
    };

    public static IReadOnlyCollection<string> All => ResourceTypes.Keys;

    public static bool IsKnown(string? verb) => verb is not null && ResourceTypes.ContainsKey(verb);

    /// <summary>null si el verbo no es del contrato.</summary>
    public static string? ResourceTypeOf(string? verb) =>
        verb is not null && ResourceTypes.TryGetValue(verb, out var type) ? type : null;
}
