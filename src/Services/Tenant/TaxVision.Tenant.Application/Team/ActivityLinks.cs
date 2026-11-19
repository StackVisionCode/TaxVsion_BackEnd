using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team;

/// <summary>Ruta del CRM de cada recurso; el módulo dueño revalida al abrir. null = no hay pantalla a la que ir.</summary>
public static class ActivityLinks
{
    public static string? For(string resourceType, Guid resourceId, Guid? customerId) =>
        resourceType switch
        {
            ActivityResourceTypes.Task => $"/task?taskId={resourceId}",
            ActivityResourceTypes.ClientRequest => OnCustomer(customerId, $"tab=work&requestId={resourceId}"),
            ActivityResourceTypes.FormRequest => $"/client-forms?formRequestId={resourceId}",
            ActivityResourceTypes.Appointment => $"/calendar?appointmentId={resourceId}",
            ActivityResourceTypes.Note => $"/notes?note={resourceId}",
            ActivityResourceTypes.Customer => $"/clients/{resourceId}",
            ActivityResourceTypes.SignatureRequest => $"/signature/requests/{resourceId}",
            ActivityResourceTypes.Invoice => $"/billing?invoiceId={resourceId}",
            ActivityResourceTypes.SmsMessage => OnCustomer(customerId, "tab=sms"),
            // Un correo nuevo aún no tiene conversación: se abre el correo del cliente (o el de no clientes).
            ActivityResourceTypes.EmailMessage => customerId is { } c ? $"/email?client={c}" : "/email?nc=1",
            ActivityResourceTypes.EmailThread => $"/email?threadId={resourceId}",
            ActivityResourceTypes.Document => OnCustomer(customerId, "tab=documents"),
            ActivityResourceTypes.SupportTicket => "/support",
            _ => null,
        };

    private static string? OnCustomer(Guid? customerId, string query) =>
        customerId is { } id ? $"/clients/{id}?{query}" : null;
}
