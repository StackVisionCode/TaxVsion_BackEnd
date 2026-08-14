using BuildingBlocks.Results;
using TaxVision.Calendar.Domain.Appointments;

namespace TaxVision.Calendar.Domain.ValueObjects;

/// <summary>
/// Nombre y correo del asistente tal como estaban el dia de la cita.
///
/// <para>
/// No es una clave foranea a Customer ni a Auth a proposito: la cita del ano pasado tiene que mostrar
/// el nombre que la persona tenia entonces, y crear una cita no puede depender de que otro servicio
/// conteste.
/// </para>
/// </summary>
public sealed record AttendeeSnapshot
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 320;

    public string DisplayName { get; }

    public string? Email { get; }

    private AttendeeSnapshot(string displayName, string? email)
    {
        DisplayName = displayName;
        Email = email;
    }

    public static Result<AttendeeSnapshot> Create(string? displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<AttendeeSnapshot>(AppointmentErrors.AttendeeNameEmpty);

        var name = displayName.Trim();
        if (name.Length > MaxNameLength)
            return Result.Failure<AttendeeSnapshot>(AppointmentErrors.AttendeeNameTooLong);

        if (string.IsNullOrWhiteSpace(email))
            return Result.Success(new AttendeeSnapshot(name, null));

        var trimmedEmail = email.Trim();
        if (trimmedEmail.Length > MaxEmailLength || !IsPlausibleEmail(trimmedEmail))
            return Result.Failure<AttendeeSnapshot>(AppointmentErrors.AttendeeEmailInvalid);

        return Result.Success(new AttendeeSnapshot(name, trimmedEmail));
    }

    /// <summary>
    /// Una instancia nueva con los mismos valores. EF no admite la misma instancia de un owned type en
    /// dos propietarios: la persiste en uno y deja el otro en NULL, y el test que compara valores pasa
    /// igual.
    /// </summary>
    internal AttendeeSnapshot Copy() => new(DisplayName, Email);

    /// <summary>
    /// Comprobacion de forma, no de existencia: el unico juez de si un correo existe es mandarlo.
    /// </summary>
    private static bool IsPlausibleEmail(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
            return false;

        var dot = value.IndexOf('.', at);
        return dot > at + 1 && dot < value.Length - 1;
    }
}
