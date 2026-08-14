namespace BuildingBlocks.Messaging.TasksIntegrationEvents;

/// <summary><c>task.created.v1</c></summary>
public sealed record TaskCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }

    /// <summary>Nombre del valor del enum, no su número: el número es detalle de persistencia.</summary>
    public required string Priority { get; init; }
    public Guid? AssigneeUserId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? TaxYear { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public Guid? ParentTaskId { get; init; }
}

/// <summary><c>task.assigned.v1</c></summary>
public sealed record TaskAssignedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required Guid AssigneeUserId { get; init; }
    public Guid? PreviousAssigneeUserId { get; init; }
    public DateTime? DueAtUtc { get; init; }
}

/// <summary><c>task.completed.v1</c></summary>
public sealed record TaskCompletedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required Guid CompletedByUserId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public Guid? CustomerId { get; init; }
    public int? TaxYear { get; init; }
}

/// <summary><c>task.reopened.v1</c></summary>
public sealed record TaskReopenedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required Guid ReopenedByUserId { get; init; }
    public required DateTime ReopenedAtUtc { get; init; }
}

/// <summary><c>task.cancelled.v1</c></summary>
public sealed record TaskCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Reason { get; init; }
    public required Guid CancelledByUserId { get; init; }
}

/// <summary><c>task.due_changed.v1</c></summary>
public sealed record TaskDueChangedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public DateTime? PreviousDueAtUtc { get; init; }
    public DateTime? NewDueAtUtc { get; init; }
    public string? TimeZoneId { get; init; }
}

/// <summary>
/// <c>task.waiting_on_client.v1</c> — se le pidió algo al cliente. <c>ExpectedItems</c> viaja hasta
/// el correo: sin él el aviso diría «tu contador necesita algo» y el cliente no podría actuar.
/// </summary>
public sealed record TaskWaitingOnClientIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required Guid CustomerId { get; init; }
    public required string ExpectedItems { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required DateTime RequestedAtUtc { get; init; }
    public int? TaxYear { get; init; }
    public DateTime? ClientDueAtUtc { get; init; }
}

/// <summary>
/// <c>task.unblocked.v1</c> — la última predecesora abierta se cerró y la tarea ya puede empezar.
/// Sin este evento el desbloqueo es invisible hasta que alguien refresca la lista.
/// </summary>
public sealed record TaskUnblockedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public Guid? AssigneeUserId { get; init; }
}

/// <summary><c>task.attachment_added.v1</c></summary>
public sealed record TaskAttachmentAddedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required Guid AttachmentId { get; init; }
    public required Guid FileId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary><c>Linked</c> nace disponible; <c>Uploaded</c> espera el escaneo.</summary>
    public required string Origin { get; init; }
    public required string Status { get; init; }
    public required Guid AttachedByUserId { get; init; }
}

/// <summary><c>task.attachment_detached.v1</c> — el archivo sigue en CloudStorage.</summary>
public sealed record TaskAttachmentDetachedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required Guid AttachmentId { get; init; }
    public required Guid FileId { get; init; }

    /// <summary>Lo quitó el usuario, o lo borraron desde CloudStorage.</summary>
    public required bool DeletedAtSource { get; init; }
}

/// <summary>
/// <c>task.attachment_rejected.v1</c> — el escaneo lo rechazó, posiblemente después de que la tarea
/// se cerró. Lleva a quien lo adjuntó porque para entonces nadie mira ya esa tarea.
/// </summary>
public sealed record TaskAttachmentRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string TaskTitle { get; init; }
    public required Guid AttachmentId { get; init; }
    public required Guid FileId { get; init; }
    public required string DisplayName { get; init; }
    public required string Reason { get; init; }
    public required Guid AttachedByUserId { get; init; }
}

/// <summary><c>task.client_request_created.v1</c> — la firma le pidió algo al cliente.</summary>
public sealed record ClientRequestCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid ClientRequestId { get; init; }
    public required Guid CustomerId { get; init; }
    public Guid? TaskId { get; init; }
    public required string Title { get; init; }
    public string? Details { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public required Guid RequestedByUserId { get; init; }
}

/// <summary>
/// <c>task.client_request_fulfilled.v1</c> — el cliente subió algo. Avisa al preparador; la tarea en
/// <c>WaitingOnClient</c> no se mueve sola: «apareció un archivo» no es «mandó lo que le pedí».
/// </summary>
public sealed record ClientRequestFulfilledIntegrationEvent : IntegrationEvent
{
    public required Guid ClientRequestId { get; init; }
    public required Guid CustomerId { get; init; }
    public Guid? TaskId { get; init; }
    public required string Title { get; init; }
    public required Guid RequestedByUserId { get; init; }
    public required int DocumentCount { get; init; }
}

/// <summary>
/// <c>task.client_request_document_rejected.v1</c> — el escaneo tumbó un documento del cliente.
///
/// <para>
/// Lleva <see cref="Reason"/> para el preparador y <see cref="ClientMessage"/> para el cliente. Son
/// dos textos distintos a propósito: «tiene un virus» no le dice al cliente qué hacer y regala
/// información de la infraestructura; «no pudimos procesarlo, vuelve a subirlo» sí es accionable.
/// </para>
/// </summary>
public sealed record ClientRequestDocumentRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid ClientRequestId { get; init; }
    public required Guid CustomerId { get; init; }
    public Guid? TaskId { get; init; }
    public required Guid FileId { get; init; }
    public required string DisplayName { get; init; }
    public required string Reason { get; init; }
    public required string ClientMessage { get; init; }
    public required Guid RequestedByUserId { get; init; }
}

/// <summary>
/// <c>task.overdue.v1</c> — la tarea pasó su vencimiento y sigue abierta.
///
/// <para>
/// Se publica <b>una sola vez</b> por vencimiento: la tarea seguirá vencida mañana y el barrido pasa
/// cada hora. Si alguien mueve la fecha, la marca se limpia y vuelve a avisarse.
/// </para>
/// </summary>
public sealed record TaskOverdueIntegrationEvent : IntegrationEvent
{
    public required Guid TaskId { get; init; }
    public required string Title { get; init; }
    public required DateTime DueAtUtc { get; init; }
    public required bool IsStatutory { get; init; }
    public Guid? AssigneeUserId { get; init; }
    public Guid? CustomerId { get; init; }
    public int? TaxYear { get; init; }
}
