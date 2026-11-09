using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.Compose;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>Lo que el hilo necesita saber del correo que entra.</summary>
public sealed record InboundThreadMessage(
    Guid TenantId,
    Guid? CustomerId,
    string Subject,
    string? ProviderThreadId,
    DateTime ReceivedAtUtc
);

/// <summary>
/// El hilo elegido para un correo. <see cref="Thread"/> ya tiene Id para armar el correo, y un hilo existente sigue
/// intacto hasta <see cref="Commit"/>: un rechazo no deja hilos vacíos ni contadores inflados.
/// </summary>
public sealed class EmailThreadAttachment
{
    private readonly bool _appends;
    private readonly Draft? _sentDraft;
    private readonly InboundThreadMessage _message;

    internal EmailThreadAttachment(EmailThread thread, bool isNewThread, Draft? sentDraft, InboundThreadMessage message)
    {
        Thread = thread;
        IsNewThread = isNewThread;
        _appends = !isNewThread || sentDraft is not null;
        _sentDraft = sentDraft;
        _message = message;
    }

    public EmailThread Thread { get; }

    /// <summary>El hilo no existía: hay que agregarlo al repositorio.</summary>
    public bool IsNewThread { get; }

    /// <summary>Enlaza el enviado y agrega el mensaje. Devuelve si el hilo estaba archivado y se reabrió.</summary>
    public Result<bool> Commit()
    {
        if (_sentDraft is not null)
        {
            var linked = _sentDraft.LinkToThread(Thread.Id);
            if (linked.IsFailure)
                return Result.Failure<bool>(linked.Error);
        }

        if (!_appends)
            return Result.Success(false);

        var appended = Thread.AppendMessage(_message.ReceivedAtUtc);
        if (appended.IsFailure)
            return appended;

        // COR-16: el hilo de un enviado nace sin id del proveedor; se completa con el de la respuesta.
        Thread.AdoptProviderThreadId(_message.ProviderThreadId);
        return appended;
    }
}

/// <summary>Decide si un correo entrante se agrega a un hilo o abre uno nuevo.</summary>
public static class EmailThreadIngestion
{
    /// <param name="matched">El hilo que coincidió, si hubo.</param>
    /// <param name="unthreadedSentDraft">COR-16: el correo del CRM que esto responde y todavía no tiene hilo.</param>
    public static Result<EmailThreadAttachment> Attach(
        EmailThread? matched,
        Draft? unthreadedSentDraft,
        InboundThreadMessage message
    )
    {
        if (matched is not null)
            return Result.Success(new EmailThreadAttachment(matched, false, unthreadedSentDraft, message));

        // Mismo id que usa el aviso de no entrega, así los dos caminos no crean dos hilos.
        var created = unthreadedSentDraft is not null
            ? EmailThread.NewForSentEmail(
                unthreadedSentDraft.Id,
                message.TenantId,
                message.CustomerId,
                unthreadedSentDraft.Subject,
                unthreadedSentDraft.UpdatedAtUtc
            )
            : EmailThread.NewFromMessage(
                message.TenantId,
                message.CustomerId,
                message.Subject,
                message.ProviderThreadId,
                message.ReceivedAtUtc
            );

        return created.IsFailure
            ? Result.Failure<EmailThreadAttachment>(created.Error)
            : Result.Success(new EmailThreadAttachment(created.Value, true, unthreadedSentDraft, message));
    }
}
