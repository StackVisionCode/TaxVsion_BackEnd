namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

public sealed record SendCorrespondenceMessageResult(Guid SentMessageId, string? ProviderMessageId);
