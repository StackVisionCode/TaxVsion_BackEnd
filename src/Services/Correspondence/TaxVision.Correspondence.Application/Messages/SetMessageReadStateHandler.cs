using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Messages;

/// <summary>
/// Marca un correo inbound como leído/no-leído (estado compartido por el tenant) — HTTP-triggered,
/// no un consumer Wolverine, mismo criterio que el resto de handlers del inbox (no empuja
/// correlación). Idempotente: <see cref="Domain.Inbox.IncomingEmail.MarkRead"/>/<c>MarkUnread</c>
/// son no-op si ya está en ese estado, así que repetir la acción no reescribe ni falla.
/// </summary>
public static class SetMessageReadStateHandler
{
    public static async Task<Result> Handle(
        SetMessageReadStateCommand command,
        IIncomingEmailRepository incomingEmails,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var email = await incomingEmails.GetByIdAsync(command.TenantId, command.IncomingEmailId, ct);
        if (email is null)
            return Result.Failure(new Error("IncomingEmail.NotFound", "The message was not found for this tenant."));

        var changed = command.IsRead ? email.MarkRead(DateTime.UtcNow) : email.MarkUnread();
        if (changed)
            await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
