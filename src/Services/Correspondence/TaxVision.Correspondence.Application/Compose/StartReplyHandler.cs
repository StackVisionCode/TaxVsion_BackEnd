using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Arranca (o reutiliza) un reply sobre un <see cref="IncomingEmail"/> ya persistido.
///
/// <para>
/// A diferencia de <see cref="Ingest.ThreadResolver"/> (Fase 6), este handler NO busca un thread
/// entre varios candidatos — ya sabe exactamente cuál es (el usuario clickeó "responder" sobre
/// un mensaje puntual, identificado por <see cref="StartReplyCommand.IncomingEmailId"/>), así que
/// arma el <see cref="ReplyContext"/> leyendo directamente los campos ya persistidos del
/// <see cref="IncomingEmail"/> (<c>InternetMessageId</c>/<c>References</c>/<c>ProviderMessageId</c>/
/// <c>EmailThreadId</c>) en vez de re-resolver threading contra el repositorio de hilos.
/// <see cref="Ingest.ThreadResolver"/> no se reusa acá — reusarlo forzaría una búsqueda que este
/// caso de uso no necesita (ver reporte de la Fase 10 para el detalle de la decisión).
/// </para>
///
/// <para>HTTP-triggered (no consumer Wolverine) — no empuja correlación, mismo criterio que el resto de los handlers de este servicio (<see cref="Threads.ArchiveThreadHandler"/>, <see cref="Messages.DownloadAttachmentHandler"/>, etc.).</para>
/// </summary>
public static class StartReplyHandler
{
    public static async Task<Result<StartReplyResult>> Handle(
        StartReplyCommand command,
        IIncomingEmailRepository incomingEmails,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var incomingEmail = await incomingEmails.GetByIdAsync(command.TenantId, command.IncomingEmailId, ct);
        if (incomingEmail is null)
            return Result.Failure<StartReplyResult>(
                new Error("IncomingEmail.NotFound", "The message being replied to was not found for this tenant.")
            );

        var existingDraft = await drafts.FindOpenReplyDraftAsync(
            command.TenantId,
            incomingEmail.CustomerId,
            incomingEmail.Id,
            ct
        );
        if (existingDraft is not null)
            return Result.Success(ToResult(existingDraft));

        return await CreateNewReplyAsync(command, incomingEmail, drafts, unitOfWork, ct);
    }

    private static async Task<Result<StartReplyResult>> CreateNewReplyAsync(
        StartReplyCommand command,
        IncomingEmail incomingEmail,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var replyContextResult = BuildReplyContext(incomingEmail);
        if (replyContextResult.IsFailure)
            return Result.Failure<StartReplyResult>(replyContextResult.Error);

        var draftResult = Draft.CreateReply(
            command.TenantId,
            incomingEmail.CustomerId,
            command.AccountId,
            replyContextResult.Value,
            incomingEmail.Subject
        );
        if (draftResult.IsFailure)
            return Result.Failure<StartReplyResult>(draftResult.Error);

        var draft = draftResult.Value;
        await drafts.AddAsync(draft, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResult(draft));
    }

    private static Result<ReplyContext> BuildReplyContext(IncomingEmail incomingEmail) =>
        ReplyContext.Create(
            incomingEmail.Id,
            incomingEmail.EmailThreadId,
            incomingEmail.InternetMessageId,
            BuildReferencesChain(incomingEmail.References, incomingEmail.InternetMessageId),
            incomingEmail.ProviderMessageId
        );

    /// <summary>
    /// Arma la cadena de <c>References</c> que se congela en el <see cref="ReplyContext"/> del reply
    /// nuevo — no es simplemente la cadena del <see cref="IncomingEmail"/> respondido copiada tal
    /// cual (eso perdería, en un reply-a-un-reply-a-un-reply, la identidad exacta de cada mensaje
    /// intermedio). Es la cadena de <paramref name="existingReferencesCsv"/> (la del mensaje
    /// respondido, "B") MÁS el propio <paramref name="repliedMessageInternetMessageId"/> de B
    /// agregado al final — B mismo pasa a formar parte del historial de la conversación para
    /// cualquier mensaje que venga después de este reply. Comportamiento estándar de threading
    /// RFC 2822/5322: <c>References</c> crece un elemento por cada hop, nunca se copia sin cambios.
    /// </summary>
    private static List<string>? BuildReferencesChain(
        string? existingReferencesCsv,
        string? repliedMessageInternetMessageId
    )
    {
        // Defensivo: un IncomingEmail persistido siempre debería tener InternetMessageId, pero si
        // faltara, no hay nada que agregar — se cae al comportamiento de forwarding preexistente.
        if (string.IsNullOrEmpty(repliedMessageInternetMessageId))
            return SplitReferences(existingReferencesCsv);

        var chain = SplitReferences(existingReferencesCsv) ?? [];
        chain.Add(repliedMessageInternetMessageId);
        return chain;
    }

    /// <summary>
    /// Inverso de <c>RawMessageReceivedConsumer.JoinReferences</c> (Fase 4/6):
    /// <see cref="IncomingEmail.References"/> se persiste como csv, <see cref="ReplyContext.References"/>
    /// vuelve a ser una lista (ver comentario de clase en <see cref="ReplyContext"/>).
    /// </summary>
    private static List<string>? SplitReferences(string? references) =>
        string.IsNullOrEmpty(references) ? null : new List<string>(references.Split(','));

    private static StartReplyResult ToResult(Draft draft) => new(draft.Id, draft.Subject, draft.ReplyContext!);
}
