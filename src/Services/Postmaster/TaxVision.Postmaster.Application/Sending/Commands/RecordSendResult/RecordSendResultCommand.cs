using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.RecordSendResult;

/// <summary>
/// Resultado del proveedor, en su propia transacción (PMS-01).
/// </summary>
public sealed record RecordSendResultCommand(
    Guid SentMessageId,
    SendResult Result,
    string FallbackFailureReason,
    MailboxFallback? Fallback
);

/// <summary>El buzón falló y el correo salió (o se intentó) por el proveedor del sistema.</summary>
public sealed record MailboxFallback(
    string FailureReason,
    string SystemProviderCode,
    string SystemFromAddress,
    string? FromDisplayName,
    string ReplyTo,
    DateTime AtUtc
)
{
    public Result ApplyTo(SentMessage message) =>
        message.FallBackToSystemProvider(
            FailureReason,
            SystemProviderCode,
            SystemFromAddress,
            FromDisplayName,
            ReplyTo,
            AtUtc
        );
}
