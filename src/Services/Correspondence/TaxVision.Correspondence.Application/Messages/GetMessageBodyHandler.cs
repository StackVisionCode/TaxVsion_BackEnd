using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;

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
        IConnectorsClient connectorsClient,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var emailResult = await LoadEmailAsync(query, incomingEmails, ct);
        if (emailResult.IsFailure)
            return Result.Failure<MessageBodyResult>(emailResult.Error);
        var email = emailResult.Value;

        var bodyResult = await connectorsClient.FetchMessageBodyAsync(
            query.TenantId,
            email.AccountId,
            email.ProviderMessageId,
            ct
        );
        if (bodyResult.IsFailure)
            return Result.Failure<MessageBodyResult>(bodyResult.Error);

        await MarkFetchedAsync(email, unitOfWork, ct);

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

    private static async Task MarkFetchedAsync(IncomingEmail email, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        email.MarkBodyFetched();
        await unitOfWork.SaveChangesAsync(ct);
    }
}
