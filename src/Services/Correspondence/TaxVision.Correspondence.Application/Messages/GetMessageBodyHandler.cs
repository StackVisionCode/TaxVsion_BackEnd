using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using Wolverine;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Body fetch bajo demanda (Fase 5) — HTTP-triggered, no un consumer Wolverine, por eso no
/// empuja correlación (mismo criterio que <c>SendCorrespondenceMessageHandler</c> de Postmaster).
/// El body de Connectors nunca se persiste acá (plan de diseño §17): <see cref="IncomingEmail.MarkBodyFetched"/>
/// solo marca "ya se abrió al menos una vez", el HTML/texto real fluye directo a la respuesta HTTP.
/// </summary>
public static class GetMessageBodyHandler
{
    public static async Task<Result<MessageBodyResult>> Handle(
        GetMessageBodyQuery query,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository emailThreads,
        IConnectorsClient connectorsClient,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var emailResult = await LoadEmailAsync(query, incomingEmails, ct);
        if (emailResult.IsFailure)
            return Result.Failure<MessageBodyResult>(emailResult.Error);
        var email = emailResult.Value;

        if (
            !await MailboxVisibility.CanSeeMessageAsync(
                query.VisibleAccountIds,
                query.TenantId,
                email,
                emailThreads,
                ct
            )
        )
            return Result.Failure<MessageBodyResult>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            );

        var bodyResult = await connectorsClient.FetchMessageBodyAsync(
            query.TenantId,
            email.AccountId,
            email.ProviderMessageId,
            ct
        );
        if (bodyResult.IsFailure)
            return Result.Failure<MessageBodyResult>(bodyResult.Error);

        if (await MarkFetchedAndReadAsync(email, unitOfWork, ct))
        {
            var thread = await emailThreads.GetByIdAsync(query.TenantId, email.EmailThreadId, ct);
            if (thread is not null)
                await ThreadStateNotifier.PublishAsync(
                    bus,
                    incomingEmails,
                    thread,
                    query.ActorUserId,
                    query.CorrelationId,
                    ct
                );
        }

        var body = bodyResult.Value;
        return Result.Success(new MessageBodyResult(body.HtmlBody, body.TextBody, body.Headers));
    }

    private static async Task<Result<IncomingEmail>> LoadEmailAsync(
        GetMessageBodyQuery query,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(query.TenantId, query.IncomingEmailId, ct);
        return email is null
            ? Result.Failure<IncomingEmail>(
                new Error("IncomingEmail.NotFound", "The message was not found for this tenant.")
            )
            : Result.Success(email);
    }

    // Abrir el cuerpo ES leerlo: se marca leído acá (estado compartido por el tenant) además de
    // registrar el body-fetch. Ambos son idempotentes, así que reabrir un correo ya leído no reescribe.
    // Devuelve si pasó de no leído a leído (solo entonces cambia el estado visible del hilo).
    private static async Task<bool> MarkFetchedAndReadAsync(
        IncomingEmail email,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        email.MarkBodyFetched();
        var becameRead = email.MarkRead(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return becameRead;
    }
}
