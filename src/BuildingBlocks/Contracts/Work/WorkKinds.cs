namespace BuildingBlocks.Contracts.Work;

/// <summary>Tipos de trabajo estables (kebab-case): el CRM los usa como clave, no se renombran.</summary>
public static class WorkKinds
{
    public const string Tasks = "tasks";
    public const string ClientRequests = "client-requests";
    public const string FormRequests = "form-requests";
    public const string Appointments = "appointments";
    public const string Meetings = "meetings";
    public const string Signatures = "signatures";
    public const string SupportTickets = "support-tickets";
    public const string ClientsPreparer = "clients-preparer";
    public const string ClientsAssigned = "clients-assigned";

    public static IReadOnlyList<string> All { get; } =
    [
        Tasks,
        ClientRequests,
        FormRequests,
        Appointments,
        Meetings,
        Signatures,
        SupportTickets,
        ClientsPreparer,
        ClientsAssigned,
    ];
}
