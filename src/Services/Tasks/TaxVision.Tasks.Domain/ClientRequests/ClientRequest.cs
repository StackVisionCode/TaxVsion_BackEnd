using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.ClientRequests.Events;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Domain.ClientRequests;

public enum ClientRequestStatus
{
    /// <summary>Se le pidió y todavía no mandó nada.</summary>
    Pending = 1,

    /// <summary>El cliente subió algo. **No** significa que sirva: eso lo dice el preparador.</summary>
    Submitted = 2,

    Accepted = 3,
    Rejected = 4,
    Cancelled = 5,
}

/// <summary>
/// Lo que la firma le pide al cliente, en el idioma del cliente. Es un agregado aparte de
/// <c>TaskItem</c> y no una vista suya: el cliente no estima horas, no imputa tiempo, no reasigna y
/// no tiene por qué ver el asignado ni las notas internas del encargo.
///
/// <para>
/// Mezclarlos, además, corrompe las dos métricas a la vez: la capacidad del staff y la
/// responsividad del cliente dejan de medir lo que dicen medir.
/// </para>
/// </summary>
public sealed class ClientRequest : AggregateRoot
{
    private readonly List<ClientRequestDocument> _documents = [];

    private ClientRequest() { }

    public Guid CustomerId { get; private set; }

    /// <summary>De qué encargo nació. Nulo si la firma pidió algo suelto, sin tarea detrás.</summary>
    public Guid? TaskId { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Details { get; private set; }

    public ClientRequestStatus Status { get; private set; }

    /// <summary>La fecha que se le dio al cliente, que no es la del encargo interno.</summary>
    public DateTime? DueAtUtc { get; private set; }

    public IReadOnlyList<ClientRequestDocument> Documents => _documents.AsReadOnly();

    public Guid RequestedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public const int MaxDocuments = 20;

    public bool IsOpen => Status is ClientRequestStatus.Pending or ClientRequestStatus.Submitted;

    public static Result<ClientRequest> Create(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        Guid? taskId,
        string? title,
        string? details,
        DateTime? dueAtUtc,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || requestedByUserId == Guid.Empty)
            return Result.Failure<ClientRequest>(TaskErrors.OwnerRequired);

        if (customerId == Guid.Empty)
            return Result.Failure<ClientRequest>(ClientRequestErrors.CustomerRequired);

        var trimmed = title?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Result.Failure<ClientRequest>(ClientRequestErrors.TitleRequired);

        if (trimmed.Length > 200)
            return Result.Failure<ClientRequest>(ClientRequestErrors.TitleTooLong);

        if (dueAtUtc is { } due && due.Kind != DateTimeKind.Utc)
            return Result.Failure<ClientRequest>(ClientRequestErrors.DueNotUtc);

        var request = new ClientRequest
        {
            CustomerId = customerId,
            RequestedByUserId = requestedByUserId,
            TaskId = taskId,
            Title = trimmed,
            Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
            DueAtUtc = dueAtUtc,
            Status = ClientRequestStatus.Pending,
            CreatedAtUtc = nowUtc,
        };
        request.SetTenant(tenantId);

        return Result.Success(request);
    }

    /// <summary>
    /// El cliente subió un archivo. El pedido pasa a <c>Submitted</c>, que es «mandó algo», no «ya
    /// está»: cerrarlo es decisión del preparador —el mismo criterio por el que nada saca una tarea
    /// de <c>WaitingOnClient</c> automáticamente—.
    /// </summary>
    public Result<ClientRequestDocument> SubmitDocument(
        Guid fileId,
        string? displayName,
        string? contentType,
        long sizeBytes,
        DateTime nowUtc
    )
    {
        if (!IsOpen)
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.Closed);

        if (fileId == Guid.Empty)
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.FileRequired);

        var name = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.FileNameRequired);

        if (name.Length > 260)
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.FileNameTooLong);

        if (_documents.Any(d => d.FileId == fileId && d.IsActive))
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.DuplicateDocument);

        if (_documents.Count(d => d.IsActive) >= MaxDocuments)
            return Result.Failure<ClientRequestDocument>(ClientRequestErrors.TooManyDocuments);

        var document = ClientRequestDocument.Upload(fileId, name, contentType, sizeBytes, nowUtc);
        document.AttachTo(Id);
        _documents.Add(document);

        var wasPending = Status == ClientRequestStatus.Pending;
        Status = ClientRequestStatus.Submitted;
        SubmittedAtUtc ??= nowUtc;

        if (wasPending)
            AddDomainEvent(new ClientRequestSubmittedDomainEvent(Id, TenantId, CustomerId, TaskId, Title, nowUtc));

        return Result.Success(document);
    }

    /// <summary>El preparador da por bueno lo que llegó. Sólo desde <c>Submitted</c>.</summary>
    public Result Accept(Guid byUserId, string? note, DateTime nowUtc)
    {
        if (Status != ClientRequestStatus.Submitted)
            return Result.Failure(ClientRequestErrors.NothingSubmitted);

        Resolve(ClientRequestStatus.Accepted, byUserId, note, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// No sirve lo que mandó. El motivo es obligatorio: sin él, el cliente recibe un «rechazado» y
    /// no sabe qué corregir, que es como no avisarle.
    /// </summary>
    public Result Reject(Guid byUserId, string? reason, DateTime nowUtc)
    {
        if (Status != ClientRequestStatus.Submitted)
            return Result.Failure(ClientRequestErrors.NothingSubmitted);

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ClientRequestErrors.RejectionReasonRequired);

        Resolve(ClientRequestStatus.Rejected, byUserId, reason.Trim(), nowUtc);
        return Result.Success();
    }

    /// <summary>Ya no hace falta: se retiró el encargo o el cliente lo trajo por otro lado.</summary>
    public Result Cancel(Guid byUserId, string? note, DateTime nowUtc)
    {
        if (!IsOpen)
            return Result.Failure(ClientRequestErrors.Closed);

        Resolve(ClientRequestStatus.Cancelled, byUserId, note, nowUtc);
        return Result.Success();
    }

    /// <summary>Idempotente: el evento de CloudStorage puede llegar dos veces.</summary>
    public bool MarkDocumentAvailable(Guid fileId, DateTime nowUtc) =>
        FindActive(fileId)?.MarkAvailable(nowUtc) ?? false;

    /// <summary>
    /// El escaneo lo rechazó. El aviso al cliente sale de aquí sin el motivo técnico; el preparador
    /// sí lo recibe.
    /// </summary>
    public bool MarkDocumentRejected(Guid fileId, string reason, DateTime nowUtc)
    {
        if (FindActive(fileId) is not { } document || !document.MarkRejected(reason, nowUtc))
            return false;

        AddDomainEvent(
            new ClientRequestDocumentRejectedDomainEvent(
                Id,
                TenantId,
                CustomerId,
                TaskId,
                document.Id,
                fileId,
                document.DisplayName,
                reason,
                RequestedByUserId,
                nowUtc
            )
        );

        // Un rechazado sigue en la lista pero ya no sirve. Si no queda ninguno que pueda valer, el
        // pedido vuelve a pendiente: el cliente tiene que subir otra vez y su lista debe decírselo.
        var stillUseful = _documents.Any(d => d.Status is AttachmentStatus.Pending or AttachmentStatus.Available);

        if (!stillUseful && Status == ClientRequestStatus.Submitted)
            Status = ClientRequestStatus.Pending;

        return true;
    }

    public bool MarkDocumentDetached(Guid fileId, DateTime nowUtc) => FindActive(fileId)?.MarkDetached(nowUtc) ?? false;

    private void Resolve(ClientRequestStatus status, Guid byUserId, string? note, DateTime nowUtc)
    {
        Status = status;
        ResolvedByUserId = byUserId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedAtUtc = nowUtc;

        AddDomainEvent(
            new ClientRequestResolvedDomainEvent(
                Id,
                TenantId,
                CustomerId,
                TaskId,
                status.ToString(),
                ResolutionNote,
                byUserId,
                nowUtc
            )
        );
    }

    private ClientRequestDocument? FindActive(Guid fileId) =>
        _documents.FirstOrDefault(d => d.FileId == fileId && d.IsActive);
}
