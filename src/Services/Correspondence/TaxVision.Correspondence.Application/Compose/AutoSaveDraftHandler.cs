using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Application.Compose;

/// <summary>
/// Autoguardado parcial (Fase 11) — HTTP-triggered, no un consumer Wolverine, mismo criterio que
/// el resto de los handlers de este servicio (no empuja correlación). <see cref="Draft.AutoSave"/>
/// ya rechaza la llamada si <c>Status != Draft</c> (<c>Draft.InvalidTransition</c>, mapeado a 409
/// en <c>ErrorHttpMapping</c>) — este handler no duplica ese chequeo, solo lo propaga.
///
/// <para>
/// <see cref="Draft.AutoSave"/> solo acepta UNA lista combinada de destinatarios (reemplaza la
/// colección entera, o no la toca si es <c>null</c>). Como <see cref="AutoSaveDraftCommand"/>
/// expone To/Cc/Bcc como tres PATCH independientes, este handler los reconcilia leyendo primero
/// los destinatarios ya persistidos: para cada tipo NO incluido en el comando, conserva lo que ya
/// había; para cada tipo incluido (aunque sea una lista vacía), lo reemplaza. Si ninguno de los
/// tres vino en el comando, pasa <c>null</c> — el aggregate no toca la colección en absoluto. Esto
/// preserva la semántica PATCH campo-por-campo incluso a nivel de tipo de destinatario, no solo a
/// nivel de subject/htmlBody/textBody.
/// </para>
/// </summary>
public static class AutoSaveDraftHandler
{
    public static async Task<Result> Handle(
        AutoSaveDraftCommand command,
        IDraftRepository drafts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var draft = await drafts.GetByIdAsync(command.TenantId, command.DraftId, ct);
        if (draft is null)
            return Result.Failure(new Error("Draft.NotFound", "The draft was not found for this tenant."));

        var recipientsResult = BuildRecipients(draft, command);
        if (recipientsResult.IsFailure)
            return Result.Failure(recipientsResult.Error);

        var autoSaveResult = draft.AutoSave(
            command.Subject,
            command.HtmlBody,
            command.TextBody,
            recipientsResult.Value
        );
        if (autoSaveResult.IsFailure)
            return autoSaveResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Null si ninguno de To/Cc/Bcc vino en el comando (no tocar la colección). Si al menos uno vino, arma la lista combinada: tipos incluidos se reemplazan, tipos omitidos conservan lo ya persistido.</summary>
    private static Result<IReadOnlyList<DraftRecipientData>?> BuildRecipients(Draft draft, AutoSaveDraftCommand command)
    {
        if (command.To is null && command.Cc is null && command.Bcc is null)
            return Result.Success<IReadOnlyList<DraftRecipientData>?>(null);

        var merged = new List<DraftRecipientData>();

        var toResult = AppendType(merged, draft, command.To, EmailRecipientType.To);
        if (toResult.IsFailure)
            return Result.Failure<IReadOnlyList<DraftRecipientData>?>(toResult.Error);

        var ccResult = AppendType(merged, draft, command.Cc, EmailRecipientType.Cc);
        if (ccResult.IsFailure)
            return Result.Failure<IReadOnlyList<DraftRecipientData>?>(ccResult.Error);

        var bccResult = AppendType(merged, draft, command.Bcc, EmailRecipientType.Bcc);
        if (bccResult.IsFailure)
            return Result.Failure<IReadOnlyList<DraftRecipientData>?>(bccResult.Error);

        return Result.Success<IReadOnlyList<DraftRecipientData>?>(merged);
    }

    /// <summary>Si <paramref name="input"/> no es null, convierte y valida esas direcciones (reemplaza ese tipo). Si es null, conserva los destinatarios ya persistidos de ese tipo tal cual estaban.</summary>
    private static Result AppendType(
        List<DraftRecipientData> merged,
        Draft draft,
        IReadOnlyList<AutoSaveDraftRecipientInput>? input,
        EmailRecipientType type
    )
    {
        if (input is null)
        {
            foreach (var existing in draft.Recipients.Where(r => r.Type == type))
            {
                // Ya fue validada al persistirse la primera vez — Create no debería fallar acá,
                // pero se chequea igual en vez de asumir (mismo criterio que DownloadAttachmentHandler
                // nunca descarta un Result de dominio en silencio).
                var existingAddress = EmailAddress.Create(existing.Address);
                if (existingAddress.IsFailure)
                    return Result.Failure(existingAddress.Error);
                merged.Add(new DraftRecipientData(existingAddress.Value, type, existing.DisplayName));
            }
            return Result.Success();
        }

        foreach (var recipient in input)
        {
            var addressResult = EmailAddress.Create(recipient.Address);
            if (addressResult.IsFailure)
                return Result.Failure(addressResult.Error);
            merged.Add(new DraftRecipientData(addressResult.Value, type, recipient.DisplayName));
        }
        return Result.Success();
    }
}
