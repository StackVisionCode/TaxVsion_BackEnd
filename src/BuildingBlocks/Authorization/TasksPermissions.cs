namespace BuildingBlocks.Authorization;

/// <summary>
/// Permisos del microservicio Task, usados como policy en <c>[HasPermission(...)]</c>.
///
/// <para>
/// Sin permiso de portal: una tarea es trabajo interno de la firma y el cliente final nunca ve la
/// lista. Lo que le llega sale por Notification, no por un endpoint de Task.
/// </para>
/// </summary>
public static class TasksPermissions
{
    /// <summary>Listar y consultar tareas del tenant.</summary>
    public const string Read = "tasks.read";

    /// <summary>
    /// Crear, editar, cerrar y reabrir tareas propias. El filtro por autoría lo aplica el handler:
    /// <c>Permission</c> no modela ownership.
    /// </summary>
    public const string Write = "tasks.write";

    /// <summary>
    /// Asignar una tarea a otra persona. Desasignarse o asignársela a uno mismo no lo necesita.
    /// </summary>
    public const string Assign = "tasks.assign";

    /// <summary>
    /// Override de supervisión: cerrar, editar o reasignar la tarea de cualquier usuario del tenant.
    /// </summary>
    public const string ManageAll = "tasks.manage_all";

    /// <summary>Crear y editar las plantillas de tarea de la firma.</summary>
    public const string TemplatesManage = "tasks.templates.manage";

    /// <summary>Pedirle documentación al cliente y cerrar lo que mande.</summary>
    public const string ClientRequestsManage = "tasks.client_requests.manage";

    /// <summary>
    /// Lo que el cliente puede hacer en su portal: ver sus pedidos y registrar lo que sube. Es el
    /// único permiso de este catálogo cuyo destinatario está fuera de la firma.
    /// </summary>
    public const string PortalClientRequests = "tasks.portal.client_requests";

    public const string FormTemplatesRead = "tasks.form_templates.read";
    public const string FormTemplatesManage = "tasks.form_templates.manage";
    public const string FormTemplatesPublish = "tasks.form_templates.publish";
    public const string FormRequestsRead = "tasks.form_requests.read";
    public const string FormRequestsManage = "tasks.form_requests.manage";
    public const string FormRequestsReview = "tasks.form_requests.review";
    public const string FormRequestsExport = "tasks.form_requests.export";
    public const string FormRequestsApplyProfile = "tasks.form_requests.apply_profile";
    public const string FormSubmissionsReveal = "tasks.form_submissions.reveal";
    public const string FormsLibraryManage = "tasks.forms.library.manage";
    public const string FormsPlatformManage = "tasks.forms.platform.manage";
    public const string PortalForms = "tasks.portal.forms";
    public const string FormTemplatesConfigureActions = "tasks.form_templates.configure_actions";
    public const string FormRequestsSendExternal = "tasks.form_requests.send_external";
    public const string FormIntakeLinksManage = "tasks.form_intake_links.manage";
    public const string FormConsentsManage = "tasks.form_consents.manage";
}
