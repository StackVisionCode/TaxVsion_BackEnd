using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tenant.Domain.Team;

/// <summary>Tipo del recurso sobre el que se actuó; mismos valores que <see cref="WorkResourceTypes"/>.</summary>
public static class ActivityResourceTypes
{
    public const string Task = WorkResourceTypes.Task;
    public const string ClientRequest = WorkResourceTypes.ClientRequest;
    public const string FormRequest = WorkResourceTypes.FormRequest;
    public const string Appointment = WorkResourceTypes.Appointment;
    public const string Note = WorkResourceTypes.Note;
    public const string Customer = WorkResourceTypes.Customer;
    public const string SignatureRequest = WorkResourceTypes.SignatureRequest;
    public const string Invoice = WorkResourceTypes.Invoice;
    public const string SmsMessage = WorkResourceTypes.SmsMessage;
    public const string EmailMessage = WorkResourceTypes.EmailMessage;
    public const string EmailThread = WorkResourceTypes.EmailThread;
    public const string Document = WorkResourceTypes.Document;
    public const string SupportTicket = WorkResourceTypes.SupportTicket;
}
