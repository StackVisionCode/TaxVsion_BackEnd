using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Domain.Inbox;

/// <summary>
/// Un correo recibido de un customer del tenant. <see cref="CustomerId"/> y
/// <see cref="EmailThreadId"/> son obligatorios por diseño — es la regla de negocio central
/// del rediseño de Correspondence: un correo cuyo sender no matcheó contra un customer nunca
/// llega a instanciar este aggregate (ver plan de diseño §2/§14).
///
/// <para>
/// A propósito, este factory NO depende del evento de integración
/// <c>connectors.raw_message_received.v1</c> (<c>ConnectorsRawMessageReceivedIntegrationEvent</c>
/// en BuildingBlocks.Messaging) — Domain no conoce contratos de mensajería. Mapear el evento
/// crudo a estos primitivos es responsabilidad del consumer de Fase 4.
/// </para>
/// </summary>
public sealed class IncomingEmail : ITenantOwned
{
    public const int ProviderCodeMaxLength = 20;
    public const int ProviderMessageIdMaxLength = 200;
    public const int InternetMessageIdMaxLength = 500;
    public const int InReplyToMaxLength = 500;
    public const int ReferencesMaxLength = 2000;
    public const int FromDisplayNameMaxLength = 200;
    public const int SubjectMaxLength = 1000;
    public const int SnippetMaxLength = 500;

    private readonly List<IncomingEmailRecipient> _recipients = [];
    private readonly List<IncomingEmailAttachment> _attachments = [];

    private IncomingEmail() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid EmailThreadId { get; private set; }

    /// <summary>RBAC Fase 5 (RBAC_Hardening_Plan.md) — ver <see cref="Compose.Draft.SetTenant"/>.</summary>
    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    /// <summary>
    /// Referencia opaca al <c>TenantEmailAccount</c> de Connectors. Sin FK ni navegación a
    /// propósito: Connectors es un microservicio distinto, Correspondence nunca lo resuelve
    /// por su cuenta, solo lo guarda para poder pedirle body/attachments más adelante.
    /// </summary>
    public Guid AccountId { get; private set; }

    /// <summary>
    /// Código de proveedor tal cual lo publica Connectors (p. ej. "gmail"/"graph"/"imap").
    /// String plano a propósito, no enum: Connectors es dueño de la lista canónica,
    /// Correspondence solo guarda lo que le dicen.
    /// </summary>
    public string ProviderCode { get; private set; } = default!;
    public string ProviderMessageId { get; private set; } = default!;
    public string? InternetMessageId { get; private set; }
    public string? InReplyTo { get; private set; }
    public string? References { get; private set; }
    public string From { get; private set; } = default!;
    public string? FromDisplayName { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Snippet { get; private set; } = default!;
    public DateTime ReceivedAtUtc { get; private set; }
    public BodyStatus BodyStatus { get; private set; }
    public DateTime? BodyFetchedAtUtc { get; private set; }
    public bool HasAttachments { get; private set; }
    public int AttachmentCount { get; private set; }

    public IReadOnlyCollection<IncomingEmailRecipient> Recipients => _recipients.AsReadOnly();
    public IReadOnlyCollection<IncomingEmailAttachment> Attachments => _attachments.AsReadOnly();

    public static Result<IncomingEmail> Create(
        Guid tenantId,
        Guid customerId,
        Guid emailThreadId,
        Guid accountId,
        string providerCode,
        string providerMessageId,
        EmailAddress from,
        string? fromDisplayName,
        string subject,
        string snippet,
        DateTime receivedAtUtc,
        bool hasAttachments,
        int attachmentCount,
        string? internetMessageId = null,
        string? inReplyTo = null,
        string? references = null,
        IReadOnlyCollection<IncomingEmailRecipientData>? recipients = null,
        IReadOnlyCollection<IncomingEmailAttachmentData>? attachments = null
    )
    {
        ArgumentNullException.ThrowIfNull(from);

        var validationError = Validate(
            tenantId,
            customerId,
            emailThreadId,
            accountId,
            providerCode,
            providerMessageId,
            subject,
            snippet,
            internetMessageId,
            inReplyTo,
            references,
            fromDisplayName
        );
        if (validationError is not null)
            return Result.Failure<IncomingEmail>(validationError);

        var email = new IncomingEmail
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            EmailThreadId = emailThreadId,
            AccountId = accountId,
            ProviderCode = providerCode,
            ProviderMessageId = providerMessageId,
            InternetMessageId = internetMessageId,
            InReplyTo = inReplyTo,
            References = references,
            From = from.NormalizedValue,
            FromDisplayName = fromDisplayName,
            Subject = subject,
            Snippet = snippet,
            ReceivedAtUtc = receivedAtUtc,
            BodyStatus = BodyStatus.BodyPending,
            BodyFetchedAtUtc = null,
            HasAttachments = hasAttachments,
            AttachmentCount = attachmentCount,
        };

        foreach (var recipient in recipients ?? [])
            email._recipients.Add(
                IncomingEmailRecipient.Create(
                    tenantId,
                    email.Id,
                    recipient.Address,
                    recipient.Type,
                    recipient.DisplayName
                )
            );

        foreach (var attachment in attachments ?? [])
            email._attachments.Add(
                IncomingEmailAttachment.Create(
                    tenantId,
                    email.Id,
                    attachment.Filename,
                    attachment.ContentType,
                    attachment.SizeBytes,
                    attachment.ProviderAttachmentId,
                    attachment.IsInline
                )
            );

        return Result.Success(email);
    }

    /// <summary>Idempotente: si el body ya se pidió al menos una vez, es un no-op.</summary>
    public void MarkBodyFetched()
    {
        if (BodyStatus == BodyStatus.BodyReady)
            return;

        BodyStatus = BodyStatus.BodyReady;
        BodyFetchedAtUtc = DateTime.UtcNow;
    }

    private static Error? Validate(
        Guid tenantId,
        Guid customerId,
        Guid emailThreadId,
        Guid accountId,
        string providerCode,
        string providerMessageId,
        string subject,
        string snippet,
        string? internetMessageId,
        string? inReplyTo,
        string? references,
        string? fromDisplayName
    )
    {
        if (tenantId == Guid.Empty)
            return new Error("IncomingEmail.TenantIdRequired", "TenantId is required.");
        if (customerId == Guid.Empty)
            return new Error("IncomingEmail.CustomerIdRequired", "CustomerId is required.");
        if (emailThreadId == Guid.Empty)
            return new Error("IncomingEmail.EmailThreadIdRequired", "EmailThreadId is required.");
        if (accountId == Guid.Empty)
            return new Error("IncomingEmail.AccountIdRequired", "AccountId is required.");
        if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Length > ProviderCodeMaxLength)
            return new Error(
                "IncomingEmail.ProviderCodeInvalid",
                "ProviderCode is required and must not exceed 20 characters."
            );
        if (string.IsNullOrWhiteSpace(providerMessageId) || providerMessageId.Length > ProviderMessageIdMaxLength)
            return new Error(
                "IncomingEmail.ProviderMessageIdInvalid",
                "ProviderMessageId is required and must not exceed 200 characters."
            );
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > SubjectMaxLength)
            return new Error(
                "IncomingEmail.SubjectInvalid",
                "Subject is required and must not exceed 1000 characters."
            );
        if (snippet is null || snippet.Length > SnippetMaxLength)
            return new Error("IncomingEmail.SnippetInvalid", "Snippet is required and must not exceed 500 characters.");
        if (internetMessageId is { Length: > InternetMessageIdMaxLength })
            return new Error(
                "IncomingEmail.InternetMessageIdTooLong",
                "InternetMessageId must not exceed 500 characters."
            );
        if (inReplyTo is { Length: > InReplyToMaxLength })
            return new Error("IncomingEmail.InReplyToTooLong", "InReplyTo must not exceed 500 characters.");
        if (references is { Length: > ReferencesMaxLength })
            return new Error("IncomingEmail.ReferencesTooLong", "References must not exceed 2000 characters.");
        if (fromDisplayName is { Length: > FromDisplayNameMaxLength })
            return new Error("IncomingEmail.FromDisplayNameTooLong", "FromDisplayName must not exceed 200 characters.");

        return null;
    }
}
