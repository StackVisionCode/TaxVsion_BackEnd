using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders;

namespace TaxVision.Reminder.Domain.ValueObjects;

/// <summary>
/// El texto que verá el usuario. Título obligatorio, cuerpo opcional.
///
/// <para>
/// <b>Es texto plano y no se sanitiza como HTML.</b> Notes sí sanitiza porque acepta contenido
/// enriquecido; acá se guarda plano y quien renderiza (Scribe) escapa. Si algún día se acepta HTML,
/// hace falta sumar un <c>IHtmlSanitizer</c> — hoy sería complejidad sin caso de uso.
/// </para>
/// </summary>
public sealed record ReminderSubject
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 2_000;

    private ReminderSubject(string title, string? body)
    {
        Title = title;
        Body = body;
    }

    public string Title { get; }
    public string? Body { get; }

    public static Result<ReminderSubject> Create(string? title, string? body)
    {
        var normalizedTitle = title?.Trim();
        if (string.IsNullOrEmpty(normalizedTitle))
            return Result.Failure<ReminderSubject>(ReminderErrors.Subject.TitleRequired);

        if (normalizedTitle.Length > MaxTitleLength)
            return Result.Failure<ReminderSubject>(ReminderErrors.Subject.TitleTooLong);

        var normalizedBody = body?.Trim();
        if (normalizedBody?.Length > MaxBodyLength)
            return Result.Failure<ReminderSubject>(ReminderErrors.Subject.BodyTooLong);

        // Un cuerpo que era sólo espacios equivale a no haber mandado cuerpo.
        return Result.Success(
            new ReminderSubject(normalizedTitle, string.IsNullOrEmpty(normalizedBody) ? null : normalizedBody)
        );
    }
}
