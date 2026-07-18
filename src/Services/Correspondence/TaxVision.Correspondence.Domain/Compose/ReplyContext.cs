using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Domain.Compose;

/// <summary>
/// Contexto de threading congelado una sola vez en <c>StartReply</c> (Fase 10), a partir de los
/// campos ya persistidos del <see cref="Inbox.IncomingEmail"/> original — el <see cref="Draft"/>
/// nunca vuelve a resolver threading después de esto, ni siquiera si el usuario tarda días en
/// enviar el reply (plan §16).
///
/// <para>
/// <see cref="References"/> se tipa como <see cref="IReadOnlyList{T}"/> de <see cref="string"/>,
/// no como csv — a diferencia de <see cref="Inbox.IncomingEmail.References"/>, que sí es csv. El
/// motivo es el consumidor real río abajo de este VO: el contrato ya implementado de
/// <c>POST /postmaster/correspondence-messages</c> (<c>CorrespondenceReplyContextRequest</c> en
/// <c>TaxVision.Postmaster.Api.Requests</c>, D3 Compose Fase 5, ya en producción) espera
/// <c>IReadOnlyList&lt;string&gt;?</c>, no un string para volver a partir. Mantenerlo como lista
/// acá evita un split/join de ida y vuelta sin ningún beneficio real.
/// </para>
/// </summary>
public sealed class ReplyContext
{
    public const int InReplyToInternetMessageIdMaxLength = 500;
    public const int ReplyToProviderMessageIdMaxLength = 200;

    private readonly List<string> _references = [];

    private ReplyContext() { }

    public Guid IncomingEmailId { get; private set; }
    public Guid EmailThreadId { get; private set; }
    public string? InReplyToInternetMessageId { get; private set; }
    public string? ReplyToProviderMessageId { get; private set; }
    public IReadOnlyList<string> References => _references.AsReadOnly();

    public static Result<ReplyContext> Create(
        Guid incomingEmailId,
        Guid emailThreadId,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId
    )
    {
        var validationError = Validate(
            incomingEmailId,
            emailThreadId,
            inReplyToInternetMessageId,
            replyToProviderMessageId
        );
        if (validationError is not null)
            return Result.Failure<ReplyContext>(validationError);

        var context = new ReplyContext
        {
            IncomingEmailId = incomingEmailId,
            EmailThreadId = emailThreadId,
            InReplyToInternetMessageId = inReplyToInternetMessageId,
            ReplyToProviderMessageId = replyToProviderMessageId,
        };

        if (references is not null)
        {
            foreach (var reference in references)
                context._references.Add(reference);
        }

        return Result.Success(context);
    }

    private static Error? Validate(
        Guid incomingEmailId,
        Guid emailThreadId,
        string? inReplyToInternetMessageId,
        string? replyToProviderMessageId
    )
    {
        if (incomingEmailId == Guid.Empty)
            return new Error("ReplyContext.IncomingEmailIdRequired", "IncomingEmailId is required.");
        if (emailThreadId == Guid.Empty)
            return new Error("ReplyContext.EmailThreadIdRequired", "EmailThreadId is required.");
        if (inReplyToInternetMessageId is { Length: > InReplyToInternetMessageIdMaxLength })
            return new Error(
                "ReplyContext.InReplyToInternetMessageIdTooLong",
                $"InReplyToInternetMessageId must not exceed {InReplyToInternetMessageIdMaxLength} characters."
            );
        if (replyToProviderMessageId is { Length: > ReplyToProviderMessageIdMaxLength })
            return new Error(
                "ReplyContext.ReplyToProviderMessageIdTooLong",
                $"ReplyToProviderMessageId must not exceed {ReplyToProviderMessageIdMaxLength} characters."
            );

        return null;
    }
}
