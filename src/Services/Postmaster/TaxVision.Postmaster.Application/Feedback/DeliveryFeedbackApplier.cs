using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Domain.Feedback;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;
using TaxVision.Postmaster.Domain.ValueObjects;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Ata cada aviso ya traducido a nuestro correo y lo aplica una sola vez. Lo comparten el webhook
/// (push) y la consulta periódica (pull): el camino es el mismo venga de donde venga.
/// </summary>
internal static class DeliveryFeedbackApplier
{
    private enum ItemOutcome
    {
        Applied,
        Duplicate,
        Unmatched,
        Invalid,
    }

    private sealed record FeedbackMatch(SentMessage Message, SentMessageRecipient Recipient);

    public static async Task<DeliveryFeedbackTally> ApplyAllAsync(
        IReadOnlyList<TranslatedFeedback> items,
        DateTime receivedAtUtc,
        DeliveryFeedbackStores stores,
        ILogger logger,
        CancellationToken ct,
        Guid? expectedTenantId = null
    )
    {
        int applied = 0,
            duplicates = 0,
            unmatched = 0,
            invalid = 0;
        foreach (var item in items)
        {
            switch (await ApplyAndPersistIdempotentlyAsync(item, receivedAtUtc, expectedTenantId, stores, logger, ct))
            {
                case ItemOutcome.Applied:
                    applied++;
                    break;
                case ItemOutcome.Duplicate:
                    duplicates++;
                    break;
                case ItemOutcome.Unmatched:
                    unmatched++;
                    break;
                default:
                    invalid++;
                    break;
            }
        }

        return new DeliveryFeedbackTally(applied, duplicates, unmatched, invalid);
    }

    private static async Task<ItemOutcome> ApplyAndPersistIdempotentlyAsync(
        TranslatedFeedback item,
        DateTime receivedAtUtc,
        Guid? expectedTenantId,
        DeliveryFeedbackStores stores,
        ILogger logger,
        CancellationToken ct
    )
    {
        var source = item.Feedback.Source.Value;
        if (await stores.Receipts.ExistsAsync(source, item.Feedback.SourceEventId, ct))
            return ItemOutcome.Duplicate;

        var match = await ResolveSentMessageAndRecipientAsync(item, stores, ct);
        // Un aviso fabricado en el buzón de una oficina no puede marcar ni suprimir correos de otra.
        if (match is not null && expectedTenantId is { } tenant && match.Message.TenantId != tenant)
        {
            logger.LogWarning(
                "Delivery feedback from {Source} points to a message of another tenant; ignored.",
                source
            );
            match = null;
        }
        if (match is null)
        {
            await RecordUnmatchedAsync(item, receivedAtUtc, stores, logger, ct);
            return ItemOutcome.Unmatched;
        }

        var applied = match.Message.ApplyDeliveryFeedback(match.Recipient.Id, item.Feedback, item.RawPayload);
        if (applied.IsFailure)
        {
            logger.LogWarning(
                "Delivery feedback from {Source} rejected by the domain: {Error}",
                source,
                applied.Error.Message
            );
            return ItemOutcome.Invalid;
        }

        await SuppressIfNeededAsync(match, item.Feedback, receivedAtUtc, stores, ct);

        var receipt = DeliveryFeedbackReceipt.Create(
            source,
            item.Feedback.SourceEventId,
            match.Message.Id,
            receivedAtUtc
        );
        if (!await stores.Receipts.TryCommitAsync(receipt, ct))
            return ItemOutcome.Duplicate;

        // Después de guardar: si se avisara antes y el guardado fallara, Notification vería algo que no pasó.
        if (applied.Value)
            await NotifyAsync(match.Message, item.Feedback, stores);
        return ItemOutcome.Applied;
    }

    private static async Task NotifyAsync(SentMessage message, DeliveryFeedback feedback, DeliveryFeedbackStores stores)
    {
        var evt = DeliveryFeedbackNotifier.EventFor(message, feedback, Guid.NewGuid().ToString("N"));
        if (evt is not null)
            await stores.Bus.PublishAsync(evt);
    }

    /// <summary>
    /// En orden de confianza: nuestro Message-ID, nuestro header, el Message-ID con el que salió y por
    /// último el id del proveedor.
    /// </summary>
    private static async Task<FeedbackMatch?> ResolveSentMessageAndRecipientAsync(
        TranslatedFeedback item,
        DeliveryFeedbackStores stores,
        CancellationToken ct
    )
    {
        Result<SentMessage>? found = null;
        if (OurMessageId.TryParse(item.MessageId, out var ourMessageId))
            found = await stores.SentMessages.GetByIdForFeedbackAsync(ourMessageId!.SentMessageId, ct);
        else if (OurMessageId.TryParseCorrelationHeader(item.CorrelationHeader, out var sentMessageId))
            found = await stores.SentMessages.GetByIdForFeedbackAsync(sentMessageId, ct);
        // El buzón cambió nuestro Message-ID (Graph pone el suyo): se busca por el que quedó.
        if (found is not { IsSuccess: true } && !string.IsNullOrWhiteSpace(item.MessageId))
            found = await stores.SentMessages.FindByInternetMessageIdForFeedbackAsync(item.MessageId, ct);
        if (found is not { IsSuccess: true } && !string.IsNullOrWhiteSpace(item.ProviderMessageId))
            found = await stores.SentMessages.FindByProviderMessageIdForFeedbackAsync(
                item.ProviderMessageId,
                item.RecipientAddress,
                ct
            );

        if (found is not { IsSuccess: true })
            return null;

        var recipient = found.Value.FindRecipientByAddress(item.RecipientAddress);
        return recipient is null ? null : new FeedbackMatch(found.Value, recipient);
    }

    /// <summary>Rebote duro o queja: no se le vuelve a escribir. Va en la misma transacción que el aviso.</summary>
    private static async Task SuppressIfNeededAsync(
        FeedbackMatch match,
        DeliveryFeedback feedback,
        DateTime receivedAtUtc,
        DeliveryFeedbackStores stores,
        CancellationToken ct
    )
    {
        SuppressionReason? reason = feedback.Outcome switch
        {
            DeliveryOutcome.HardBounced => SuppressionReason.HardBounce,
            DeliveryOutcome.Complained => SuppressionReason.Complaint,
            _ => null,
        };
        if (reason is null)
            return;

        var tenantId = match.Message.TenantId;
        var address = match.Recipient.Address;
        if ((await stores.SuppressionList.GetByAddressAsync(tenantId, address, ct)).IsSuccess)
            return;

        var entry = SuppressionListEntry.CreateFromFeedback(
            tenantId,
            address,
            reason.Value,
            feedback.Source.Value,
            match.Message.Id,
            receivedAtUtc
        );
        if (entry.IsSuccess)
            await stores.SuppressionList.AddAsync(entry.Value, ct);
    }

    private static async Task RecordUnmatchedAsync(
        TranslatedFeedback item,
        DateTime receivedAtUtc,
        DeliveryFeedbackStores stores,
        ILogger logger,
        CancellationToken ct
    )
    {
        var maskedRecipient = EmailAddressMask.Mask(item.RecipientAddress);
        logger.LogInformation(
            "Delivery feedback from {Source} for {Recipient} did not match any sent message.",
            item.Feedback.Source.Value,
            maskedRecipient
        );

        await stores.Unmatched.AddAsync(
            UnmatchedDeliveryFeedback.Create(
                item.Feedback.Source.Value,
                item.Feedback.SourceEventId,
                maskedRecipient,
                item.MessageId ?? item.CorrelationHeader ?? item.ProviderMessageId,
                item.RawPayload,
                receivedAtUtc
            ),
            ct
        );
        await stores.UnitOfWork.SaveChangesAsync(ct);
    }
}
