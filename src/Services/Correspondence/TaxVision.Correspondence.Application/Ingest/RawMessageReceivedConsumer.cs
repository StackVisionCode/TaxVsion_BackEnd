using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Consume <c>connectors.raw_message_received.v1</c> (Connectors Fase 7/8) y decide si el
/// mensaje entra al inbox de un customer conocido del tenant. Flujo completo en
/// Correspondence_Service_Design_And_Implementation_Plan.md §36 Fase 4; <see cref="UnmatchedIncomingEmail"/>
/// en §14. Fetch de body (Fase 5) y descarga de attachments (Fase 8/12) están explícitamente
/// fuera de alcance de este consumer.
/// </summary>
public static class RawMessageReceivedConsumer
{
    public static async Task Handle(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        ThreadResolver threadResolver,
        ICustomerEmailAddressRepository customerEmails,
        IUnmatchedIncomingEmailRepository unmatched,
        IOptions<CorrespondenceIngestOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(evt.CorrelationId))
        {
            if (await IsDuplicateAsync(evt, incomingEmails, ct))
            {
                logger.LogDebug(
                    "Duplicate InternetMessageId {InternetMessageId} for tenant {TenantId}; skipping.",
                    evt.InternetMessageId,
                    evt.TenantId
                );
                return;
            }

            var fromResult = EmailAddress.Create(evt.From);
            if (fromResult.IsFailure)
            {
                logger.LogWarning(
                    "raw_message_received event {EventId} has an invalid From address; skipping.",
                    evt.EventId
                );
                return;
            }
            var from = fromResult.Value;

            var customer = await customerEmails.FindActiveByAddressAsync(evt.TenantId, from.NormalizedValue, ct);
            if (customer is null)
            {
                await HandleUnmatchedSenderAsync(evt, from, options.Value, unmatched, unitOfWork, logger, ct);
                return;
            }

            if (HasFailedAuthentication(evt))
            {
                await RecordUnmatchedAsync(evt, from, UnmatchedReason.AuthenticationFailed, unmatched, logger, ct);
                await unitOfWork.SaveChangesAsync(ct);
                logger.LogWarning(
                    "raw_message_received event {EventId} for customer {CustomerId} failed authentication (spf={Spf} dkim={Dkim} dmarc={Dmarc}); quarantined.",
                    evt.EventId,
                    customer.CustomerId,
                    evt.SpfResult,
                    evt.DkimResult,
                    evt.DmarcResult
                );
                return;
            }

            await IngestMatchedMessageAsync(
                evt,
                customer.CustomerId,
                from,
                incomingEmails,
                threads,
                threadResolver,
                unitOfWork,
                bus,
                logger,
                ct
            );
        }
    }

    private static async Task<bool> IsDuplicateAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        IIncomingEmailRepository incomingEmails,
        CancellationToken ct
    )
    {
        if (evt.InternetMessageId is null)
            return false;

        var existing = await incomingEmails.FindByInternetMessageIdAsync(evt.TenantId, evt.InternetMessageId, ct);
        return existing is not null;
    }

    private static async Task HandleUnmatchedSenderAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        EmailAddress from,
        CorrespondenceIngestOptions options,
        IUnmatchedIncomingEmailRepository unmatched,
        IUnitOfWork unitOfWork,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        if (!options.EnableUnmatchedDebug)
            return;

        await RecordUnmatchedAsync(evt, from, UnmatchedReason.NoCustomerMatch, unmatched, logger, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static bool HasFailedAuthentication(ConnectorsRawMessageReceivedIntegrationEvent evt) =>
        IsResult(evt.DmarcResult, "Fail") || (IsResult(evt.SpfResult, "Fail") && IsResult(evt.DkimResult, "Fail"));

    private static bool IsResult(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static async Task RecordUnmatchedAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        EmailAddress from,
        UnmatchedReason reason,
        IUnmatchedIncomingEmailRepository unmatched,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        var result = UnmatchedIncomingEmail.Create(
            evt.TenantId,
            from,
            evt.Subject,
            evt.ProviderMessageId,
            evt.ReceivedAtUtc,
            reason
        );
        if (result.IsFailure)
        {
            logger.LogWarning(
                "UnmatchedIncomingEmail.Create failed for event {EventId}: {Error}",
                evt.EventId,
                result.Error.Code
            );
            return;
        }

        await unmatched.AddAsync(result.Value, ct);
    }

    private static async Task IngestMatchedMessageAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        Guid customerId,
        EmailAddress from,
        IIncomingEmailRepository incomingEmails,
        IEmailThreadRepository threads,
        ThreadResolver threadResolver,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<IncomingEmail> logger,
        CancellationToken ct
    )
    {
        var threadResult = await ResolveOrCreateThreadAsync(evt, customerId, threadResolver, threads, ct);
        if (threadResult.IsFailure)
        {
            logger.LogWarning(
                "EmailThread resolution failed for event {EventId}: {Error}",
                evt.EventId,
                threadResult.Error.Code
            );
            return;
        }
        var thread = threadResult.Value;

        var emailResult = IncomingEmail.Create(
            evt.TenantId,
            customerId,
            thread.Id,
            evt.AccountId,
            evt.ProviderCode,
            evt.ProviderMessageId,
            from,
            fromDisplayName: null,
            evt.Subject,
            evt.Snippet,
            evt.ReceivedAtUtc,
            evt.HasAttachments,
            evt.AttachmentCount,
            evt.InternetMessageId,
            evt.InReplyTo,
            JoinReferences(evt.References),
            BuildRecipients(evt),
            BuildAttachments(evt)
        );
        if (emailResult.IsFailure)
        {
            logger.LogWarning(
                "IncomingEmail.Create failed for event {EventId}: {Error}",
                evt.EventId,
                emailResult.Error.Code
            );
            return;
        }
        var email = emailResult.Value;

        await incomingEmails.AddAsync(email, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CorrespondenceCustomerEmailReceivedIntegrationEvent
            {
                TenantId = evt.TenantId,
                CorrelationId = evt.CorrelationId,
                CustomerId = customerId,
                IncomingEmailId = email.Id,
                EmailThreadId = thread.Id,
                Subject = evt.Subject,
            }
        );
    }

    /// <summary>
    /// Delega la búsqueda a <see cref="ThreadResolver"/> (4 capas, plan §36 Fase 6) y decide
    /// acá — no en el resolver — entre <see cref="EmailThread.AppendMessage"/> sobre un match
    /// existente y <see cref="EmailThread.NewFromMessage"/> cuando ninguna capa matcheó.
    /// </summary>
    private static async Task<Result<EmailThread>> ResolveOrCreateThreadAsync(
        ConnectorsRawMessageReceivedIntegrationEvent evt,
        Guid customerId,
        ThreadResolver threadResolver,
        IEmailThreadRepository threads,
        CancellationToken ct
    )
    {
        var resolution = await threadResolver.ResolveAsync(
            evt.TenantId,
            customerId,
            evt.ProviderThreadId,
            evt.InReplyTo,
            evt.References,
            evt.Subject,
            evt.ReceivedAtUtc,
            ct
        );

        if (resolution.MatchedThread is not null)
        {
            var appendResult = resolution.MatchedThread.AppendMessage(evt.ReceivedAtUtc);
            return appendResult.IsFailure
                ? Result.Failure<EmailThread>(appendResult.Error)
                : Result.Success(resolution.MatchedThread);
        }

        var createResult = EmailThread.NewFromMessage(
            evt.TenantId,
            customerId,
            evt.Subject,
            evt.ProviderThreadId,
            evt.ReceivedAtUtc
        );
        if (createResult.IsSuccess)
            await threads.AddAsync(createResult.Value, ct);

        return createResult;
    }

    private static string? JoinReferences(IReadOnlyList<string>? references) =>
        references is null || references.Count == 0 ? null : string.Join(",", references);

    private static List<IncomingEmailRecipientData> BuildRecipients(ConnectorsRawMessageReceivedIntegrationEvent evt)
    {
        var recipients = new List<IncomingEmailRecipientData>();
        AddRecipients(recipients, evt.To, EmailRecipientType.To);
        AddRecipients(recipients, evt.Cc, EmailRecipientType.Cc);
        AddRecipients(recipients, evt.Bcc, EmailRecipientType.Bcc);
        return recipients;
    }

    private static void AddRecipients(
        List<IncomingEmailRecipientData> target,
        IReadOnlyList<string> addresses,
        EmailRecipientType type
    )
    {
        foreach (var raw in addresses)
        {
            var result = EmailAddress.Create(raw);
            if (result.IsSuccess)
                target.Add(new IncomingEmailRecipientData(result.Value, type, null));
        }
    }

    private static List<IncomingEmailAttachmentData> BuildAttachments(ConnectorsRawMessageReceivedIntegrationEvent evt)
    {
        var attachments = new List<IncomingEmailAttachmentData>();
        foreach (var meta in evt.AttachmentMetadata ?? [])
            attachments.Add(
                new IncomingEmailAttachmentData(
                    meta.Filename,
                    meta.ContentType,
                    meta.SizeBytes,
                    meta.ProviderAttachmentId,
                    false
                )
            );

        return attachments;
    }
}
