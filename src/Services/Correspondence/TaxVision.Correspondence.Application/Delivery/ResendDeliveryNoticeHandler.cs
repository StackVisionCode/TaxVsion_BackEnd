using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Delivery;

/// <summary>
/// <paramref name="Recipient"/> = dirección corregida; null = la misma del aviso. Gate = el de la conversación
/// (<paramref name="VisibleAccountIds"/>, <paramref name="RestrictToUserId"/>) más el buzón de envío.
/// </summary>
public sealed record ResendDeliveryNoticeCommand(
    Guid TenantId,
    Guid DeliveryNoticeId,
    string? Recipient,
    Guid ActorUserId,
    IReadOnlyCollection<Guid>? VisibleAccountIds = null,
    Guid? RestrictToUserId = null
);

public sealed record ResendDeliveryNoticeResult(Guid DraftId);

/// <summary>
/// "Fix address and resend" / "Resend": crea un borrador clonado del enviado que rebotó (asunto, cuerpo y los mismos
/// archivos), solo para ese destinatario y en la misma conversación. No envía: el CRM lo abre en el redactor. Dos
/// clics crean dos borradores (aceptado: un borrador se descarta sin costo).
/// <para>
/// Los adjuntos son filas nuevas que apuntan a los mismos <c>FileId</c>: Postmaster los lee de CloudStorage por
/// tenant al enviar y nadie los borra al usarlos, así que compartirlos no exige nada de CloudStorage.
/// </para>
/// </summary>
public static class ResendDeliveryNoticeHandler
{
    public static async Task<Result<ResendDeliveryNoticeResult>> Handle(
        ResendDeliveryNoticeCommand command,
        IDeliveryNoticeRepository notices,
        IEmailThreadRepository emailThreads,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var notice = await notices.GetByIdAsync(command.TenantId, command.DeliveryNoticeId, ct);
        var thread = notice is null
            ? null
            : await emailThreads.GetByIdAsync(command.TenantId, notice.EmailThreadId, ct);
        if (
            notice is null
            || thread is null
            || !await ThreadAccess.CanSeeAsync(
                emailThreads,
                thread,
                command.VisibleAccountIds,
                command.RestrictToUserId,
                ct
            )
        )
            return Result.Failure<ResendDeliveryNoticeResult>(GetDeliveryNoticeOriginalHandler.NotFound);

        var original = await drafts.GetByIdAsync(command.TenantId, notice.DraftId, ct);
        if (original is null)
            return Result.Failure<ResendDeliveryNoticeResult>(
                new Error("Draft.NotFound", "The original email no longer exists.")
            );

        // Mismo control que al redactar: solo desde un buzón al que el usuario tiene acceso.
        var accountCheck = SendingAccountGuard.Validate(original.AccountId, command.VisibleAccountIds);
        if (accountCheck.IsFailure)
            return Result.Failure<ResendDeliveryNoticeResult>(accountCheck.Error);

        var address = EmailAddress.Create(
            string.IsNullOrWhiteSpace(command.Recipient) ? notice.Recipient : command.Recipient
        );
        if (address.IsFailure)
            return Result.Failure<ResendDeliveryNoticeResult>(address.Error);

        var resend = Draft.CreateResend(original, address.Value, command.ActorUserId, thread.Id);
        if (resend.IsFailure)
            return Result.Failure<ResendDeliveryNoticeResult>(resend.Error);

        await drafts.AddAsync(resend.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ResendDeliveryNoticeResult(resend.Value.Id));
    }
}
