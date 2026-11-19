namespace TaxVision.Tenant.Domain.Team;

/// <summary>Tipo del recurso sobre el que se actuó; estable, el CRM arma el enlace con él.</summary>
public static class ActivityResourceTypes
{
    public const string Task = "task";
    public const string ClientRequest = "client_request";
    public const string FormRequest = "form_request";
    public const string Appointment = "appointment";
    public const string Note = "note";
    public const string Customer = "customer";
    public const string SignatureRequest = "signature_request";
    public const string Invoice = "invoice";
    public const string SmsMessage = "sms_message";
    public const string EmailMessage = "email_message";
    public const string EmailThread = "email_thread";
    public const string Document = "document";
    public const string SupportTicket = "support_ticket";
}
