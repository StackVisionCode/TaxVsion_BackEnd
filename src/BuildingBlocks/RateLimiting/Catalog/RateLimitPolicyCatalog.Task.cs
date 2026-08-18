namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    // Compartida por GET /tasks/{id} y GET /tasks — mismo perfil de lectura simple.
    public static readonly RateLimitPolicyDefinition TaskRead = Define(
        "task.f.read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // GET /tasks/{id}/attachments — devuelve metadatos y fileId; la descarga la sirve CloudStorage.
    public static readonly RateLimitPolicyDefinition TaskAttachmentRead = Define(
        "task.f.attachment_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // GET /tasks/waiting-on-client — pantalla de seguimiento de lo que se le pidió al cliente.
    public static readonly RateLimitPolicyDefinition TaskWaitingOnClient = Define(
        "task.f.waiting_on_client",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por POST /tasks y POST /tasks/{id}/subtasks.
    public static readonly RateLimitPolicyDefinition TaskCreate = Define(
        "task.g.create",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por PUT, start, complete, assign y unassign — escrituras simples sobre una tarea
    // existente.
    public static readonly RateLimitPolicyDefinition TaskUpdate = Define(
        "task.g.update",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // POST/DELETE /tasks/{id}/dependencies — toma un cerrojo para validar el ciclo, así que un
    // bucle acá serializa escrituras de todo el tenant.
    public static readonly RateLimitPolicyDefinition TaskDependency = Define(
        "task.g.dependency",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Adjuntar es escritura normal: el peso real (subir el byte) lo carga CloudStorage.
    public static readonly RateLimitPolicyDefinition TaskAttachment = Define(
        "task.g.attachment",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // POST /tasks/{id}/wait-on-client — escritura con efecto externo: dispara un correo al cliente.
    public static readonly RateLimitPolicyDefinition TaskWaitOnClientRequest = Define(
        "task.g.wait_on_client",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por GET /tasks/search y GET /tasks/board. En temporada fiscal estos listados se
    // piden en bucle desde el Kanban.
    public static readonly RateLimitPolicyDefinition TaskSearch = Define(
        "task.h.search",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    // GET /tasks/{id}/graph — recorre el componente conexo por SQL recursivo, caro por definición.
    public static readonly RateLimitPolicyDefinition TaskGraph = Define(
        "task.h.graph",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 20,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 100
    );

    // POST /tasks/from-template — una llamada crea N tareas y M aristas, con validación de grafo.
    //
    // La ventana es de un minuto, no de una hora como el resto de la categoría I: aplicar la
    // plantilla del 1040 a un cliente es trabajo rutinario de un preparador en enero, no una
    // operación excepcional. Con 5/hora un preparador que da de alta diez clientes en una mañana
    // quedaría bloqueado. Lo que hay que frenar es el bucle programático, y para eso el minuto
    // alcanza.
    public static readonly RateLimitPolicyDefinition TaskTemplateApply = Define(
        "task.i.template_apply",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 60
    );

    // POST /tasks/series y su pausa/reanudación/cierre. Crear una serie materializa además su primera
    // ocurrencia, así que cuesta como una escritura doble; el resto de las transiciones son baratas
    // pero comparten política porque nadie las llama en bucle.
    public static readonly RateLimitPolicyDefinition TaskSeriesWrite = Define(
        "task.h.series_write",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 150
    );

    // Editar el guion es cosa de socios y pasa poco; aplicarlo es el gesto diario del preparador, que
    // en temporada toma varios clientes seguidos.
    // El portal no comparte cuota con el staff: un cliente subiendo su W-2 no puede quedarse sin
    // turno porque la firma esté trabajando, ni al revés.
    public static readonly RateLimitPolicyDefinition TaskPortalRead = Define(
        "task.f.portal_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 120,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition TaskPortalSubmit = Define(
        "task.h.portal_submit",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 150
    );

    public static readonly RateLimitPolicyDefinition TaskClientRequestsWrite = Define(
        "task.h.client_requests_write",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 300
    );

    // Adjuntar es parte del trabajo normal sobre la tarea, no una operación de administración: el
    // preparador engancha los cuatro documentos de un cliente de una sentada.
    public static readonly RateLimitPolicyDefinition TaskAttachmentsWrite = Define(
        "task.h.attachments_write",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 300
    );

    public static readonly RateLimitPolicyDefinition TaskTemplatesWrite = Define(
        "task.h.templates_write",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 30,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 150
    );

    public static readonly RateLimitPolicyDefinition TaskTemplatesApply = Define(
        "task.h.templates_apply",
        RateLimitCategory.H,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.SlidingWindow,
        overlayQuota: 300
    );
}
