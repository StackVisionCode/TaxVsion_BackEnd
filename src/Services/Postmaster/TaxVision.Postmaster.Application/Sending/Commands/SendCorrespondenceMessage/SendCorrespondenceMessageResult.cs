namespace TaxVision.Postmaster.Application.Sending.Commands.SendCorrespondenceMessage;

/// <param name="InProgress">El envío de este draft ya está en curso o su resultado no quedó registrado: el controller responde 202.</param>
/// <param name="InternetMessageId">Message-ID real del enviado: Correspondence lo usa para enhebrar la respuesta (COR-16).</param>
public sealed record SendCorrespondenceMessageResult(
    Guid SentMessageId,
    string? ProviderMessageId,
    bool InProgress = false,
    string? InternetMessageId = null
);
