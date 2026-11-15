namespace TaxVision.Auth.Application.Offboarding;

/// <summary>
/// Un servicio que participa del retiro: qué pasos (consumers de <c>UserOffboardedIntegrationEvent</c>) debe
/// acusar y, si tiene, su pre-flight (<c>offboarding-impact</c>). <see cref="Module"/> es el nombre que ve la
/// oficina en los avisos.
/// </summary>
public sealed record OffboardingParticipant(
    string Service,
    string Module,
    IReadOnlyList<string> Steps,
    string? ImpactPath = null,
    bool AcceptsEmail = false
);

/// <summary>
/// Registro de los pasos esperados del retiro. Lo compara <c>OffboardingParticipantsContractTests</c>
/// (BuildingBlocks.Tests) con los consumers reales y los <c>offboarding-impact</c> de src/Services: un consumer
/// o pre-flight nuevo obliga a anotarlo acá. Formato fijo (una entrada <c>new(...)</c> por servicio, solo
/// literales) porque el test lo lee del fuente. <c>{userId}</c> en la ruta lo reemplaza el cliente;
/// <c>AcceptsEmail</c> = existe también <c>POST</c> con <c>{ "email" }</c> en el cuerpo.
/// </summary>
public static class OffboardingParticipants
{
    public static IReadOnlyList<OffboardingParticipant> All { get; } =
    [
        new("Auth", "Seats", ["RecomputeTenantSeatSlotsConsumer"]),
        new("Billing", "Billing", ["UserLifecyclePermissionsProjectionConsumer"]),
        new(
            "Calendar",
            "Calendar",
            [
                "CalendarUserOffboardedConsumer",
                "PublicBookingHostOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/calendar/offboarding-impact/{userId}"
        ),
        new(
            "Campaigns",
            "Campaigns",
            ["CampaignSchedulesUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"],
            "/campaigns/offboarding-impact/{userId}"
        ),
        new("Catalog", "Catalog", ["UserLifecyclePermissionsProjectionConsumer"]),
        new(
            "CloudStorage",
            "Files and cloud storage",
            [
                "CloudConnectionsUserOffboardedConsumer",
                "CloudStorageUserOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/storage/offboarding-impact/{userId}",
            AcceptsEmail: true
        ),
        new(
            "Communication",
            "Chat and meetings",
            ["offboarding-consumers"],
            "/communication/offboarding-impact/{userId}"
        ),
        new(
            "Connectors",
            "Mailboxes",
            [
                "ConnectorsUserOffboardedConsumer",
                "OfficeMailboxesUserOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/connectors/offboarding-impact/{userId}",
            AcceptsEmail: true
        ),
        new(
            "Correspondence",
            "Email",
            ["CorrespondenceUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"],
            "/correspondence/offboarding-impact/{userId}"
        ),
        new(
            "Customer",
            "Clients",
            [
                "CustomerImportsUserOffboardedConsumer",
                "CustomerUserOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/customers/offboarding-impact/{userId}"
        ),
        new("Documents", "Documents", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Growth", "Referrals", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Inventory", "Inventory", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Notes", "Notes", ["NotesUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"]),
        new(
            "Notification",
            "Notifications",
            [
                "AuthzUserLifecyclePermissionsProjectionConsumer",
                "RecipientProjectionUserOffboardedConsumer",
                "UserEmailDirectoryOffboardingConsumer",
                "UserLifecyclePushDeviceConsumer",
                "UserOffboardedPermissionsProjectionConsumer",
            ]
        ),
        new("PaymentApp", "Subscription payments", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("PaymentClient", "Client payments", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Postmaster", "Email delivery", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Reminder", "Reminders", ["ReminderUserOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"]),
        new("Scribe", "Email templates", ["UserLifecyclePermissionsProjectionConsumer"]),
        new(
            "Signature",
            "E-signatures",
            [
                "SignatureRequestsUserOffboardedConsumer",
                "SignatureUserOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/signature/offboarding-impact/{userId}"
        ),
        new("Sms", "Text messages", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("Subscription", "Subscription", ["UserLifecyclePermissionsProjectionConsumer"]),
        new(
            "Tasks",
            "Tasks and client forms",
            [
                "ClientRequestsUserOffboardedConsumer",
                "FormIntakeLinksUserOffboardedConsumer",
                "FormRequestsUserOffboardedConsumer",
                "TaskTimersUserOffboardedConsumer",
                "TasksUserOffboardedConsumer",
                "UserLifecyclePermissionsProjectionConsumer",
            ],
            "/tasks/offboarding-impact/{userId}"
        ),
        new(
            "Tenant",
            "Office settings",
            ["SecurityCoordinatorOffboardedConsumer", "UserLifecyclePermissionsProjectionConsumer"],
            "/tenants/offboarding-impact/{userId}"
        ),
        new("Wallet", "Wallet", ["UserLifecyclePermissionsProjectionConsumer"]),
        new("FinancialInsights", "Financial Insights", ["UserLifecyclePermissionsProjectionConsumer"]),
    ];

    /// <summary>Todos los pasos esperados (servicio, consumer).</summary>
    public static IEnumerable<(string Service, string Step)> ExpectedSteps =>
        All.SelectMany(p => p.Steps.Select(step => (p.Service, step)));

    /// <summary>Nombre visible del servicio, o el propio servicio si no está registrado.</summary>
    public static string ModuleOf(string service) =>
        All.FirstOrDefault(p => string.Equals(p.Service, service, StringComparison.Ordinal))?.Module ?? service;
}
