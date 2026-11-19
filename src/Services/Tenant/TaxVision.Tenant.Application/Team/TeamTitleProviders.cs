using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tenant.Application.Team;

/// <summary>
/// Registro de títulos del historial: el CRM pide a cada servicio los títulos de los recursos de una página de Activity
/// (el historial guarda solo ids). Cada servicio devuelve solo lo que quien mira puede leer. Un fitness test exige que
/// cada path exista como POST y que todos los tipos con título estén cubiertos.
/// </summary>
public static class TeamTitleProviders
{
    public static IReadOnlyList<TitleProviderDescriptor> All { get; } =
    [
        new(
            "tasks",
            [WorkResourceTypes.Task, WorkResourceTypes.ClientRequest, WorkResourceTypes.FormRequest],
            "/tasks/work/titles"
        ),
        new("calendar", [WorkResourceTypes.Appointment], "/calendar/work/titles"),
        new("signature", [WorkResourceTypes.SignatureRequest], "/signature/work/titles"),
        new("notes", [WorkResourceTypes.Note], "/notes/work/titles"),
        new("billing", [WorkResourceTypes.Invoice], "/billing/work/titles"),
        new(
            "correspondence",
            [WorkResourceTypes.EmailMessage, WorkResourceTypes.EmailThread],
            "/correspondence/work/titles"
        ),
        new("cloudstorage", [WorkResourceTypes.Document], "/storage/work/titles"),
        new("communication", [WorkResourceTypes.SupportTicket], "/communication/work/titles"),
    ];
}
